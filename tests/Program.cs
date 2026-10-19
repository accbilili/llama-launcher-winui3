using LlamaLauncher;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;

var report = new List<object>();
var settings = new Settings();
var models = ModelFile.Scan(settings.ModelRoot);
using var http = new HttpClient(new HttpClientHandler { UseProxy = false, AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate }) { Timeout = TimeSpan.FromMinutes(3) };
string reportPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "smoke-results.json");
int port = 18089;
try
{
    if (models.Count != 3) throw new Exception($"Expected three local models, found {models.Count}.");
    await using (var server = new Server())
    {
        var occupied = new TcpListener(IPAddress.Loopback, port); occupied.Start();
        try
        {
            await server.StartAsync(settings.ServerPath, new() { Port = port }, models[0].Path);
            throw new Exception("Occupied port was accepted.");
        }
        catch (IOException e) when (e.Message.Contains("占用")) { report.Add(new { test = "occupied-port", passed = true }); }
        finally { occupied.Stop(); }
        string invalid = Path.Combine(Path.GetTempPath(), "llama-launcher-invalid-" + Guid.NewGuid() + ".gguf");
        File.WriteAllText(invalid, "This is deliberately not a GGUF model.");
        try
        {
            await server.StartAsync(settings.ServerPath, new() { Port = port }, invalid);
            await WaitUntil(() => !server.Running, TimeSpan.FromSeconds(30));
            await WaitUntil(() => server.State.Contains("退出"), TimeSpan.FromSeconds(10));
            if (server.Ready || !server.State.Contains("退出")) throw new Exception("Early exit not detected.");
            report.Add(new { test = "invalid-model-early-exit", passed = true, state = server.State });
            await server.StopAsync();
        }
        finally { File.Delete(invalid); }
        foreach (var model in models)
        {
            Console.WriteLine("START " + model.Name);
            var watch = Stopwatch.StartNew();
            await server.StartAsync(settings.ServerPath, new() { Port = port, Context = 8192 }, model.Path);
            await WaitUntil(() => server.Ready || !server.Running, TimeSpan.FromMinutes(3));
            if (!server.Ready) throw new Exception("Model failed: " + model.Name + " | " + server.LogPath);
            double loadSeconds = watch.Elapsed.TotalSeconds;
            var list = await http.GetFromJsonAsync<JsonElement>(server.BaseUrl + "/v1/models");
            string id = list.GetProperty("data")[0].GetProperty("id").GetString()!;
            using var page = await http.GetAsync(server.BaseUrl + "/"); page.EnsureSuccessStatusCode();
            using var response = await http.PostAsJsonAsync(server.BaseUrl + "/v1/chat/completions", new
            {
                model = id,
                messages = new[] { new { role = "user", content = "Translate this English sentence into simplified Chinese. Output only the translation: Hello, the local model is running." } },
                max_tokens = 96, temperature = 0.1, stream = false,
                chat_template_kwargs = new { enable_thinking = false }
            });
            string json = await response.Content.ReadAsStringAsync(); response.EnsureSuccessStatusCode();
            using var body = JsonDocument.Parse(json);
            string answer = body.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
            if (string.IsNullOrWhiteSpace(answer)) throw new Exception("Empty answer: " + json);
            int? pid = server.ProcessId;
            string log = server.LogPath;
            await server.StopAsync();
            if (server.Running || server.Ready) throw new Exception("Stop failed.");
            var probe = new TcpListener(IPAddress.Loopback, port); probe.Start(); probe.Stop();
            bool vulkan = File.ReadAllText(log).Contains("Vulkan0", StringComparison.OrdinalIgnoreCase);
            report.Add(new { test = "real-model-start-infer-stop", passed = true, model = model.Name, loadSeconds, answer, processId = pid, vulkan, log });
            Console.WriteLine("PASS " + model.Name + " | " + answer);
        }
    }
    Console.WriteLine("ALL PASSED");
}
catch (Exception e)
{
    report.Add(new { test = "failure", passed = false, error = e.ToString() });
    Console.WriteLine(e); Environment.ExitCode = 1;
}
finally
{
    File.WriteAllText(Path.GetFullPath(reportPath), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine("REPORT " + Path.GetFullPath(reportPath));
}
static async Task WaitUntil(Func<bool> predicate, TimeSpan timeout)
{
    var watch = Stopwatch.StartNew();
    while (!predicate())
    {
        if (watch.Elapsed > timeout) throw new TimeoutException("Service wait exceeded " + timeout);
        await Task.Delay(250);
    }
}
