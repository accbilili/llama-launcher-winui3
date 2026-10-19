using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace LlamaLauncher;

internal sealed class Server : IAsyncDisposable
{
    private Process? process;
    private ProcessJob? job;
    private CancellationTokenSource? monitor;
    private Task monitorTask = Task.CompletedTask;
    private Task[] readers = [];
    private StreamWriter? fileLog;
    private readonly object logLock = new();
    public readonly ConcurrentQueue<string> Messages = new();
    public bool Running => process is { HasExited: false };
    public bool Ready { get; private set; }
    public string ModelPath { get; private set; } = "";
    public string ModelId => Path.GetFileNameWithoutExtension(ModelPath);
    public LaunchProfile? ActiveProfile { get; private set; }
    public string BaseUrl { get; private set; } = "";
    public string LogPath { get; private set; } = "";
    public string State { get; private set; } = "尚未启动";
    public string FailureDetails { get; private set; } = "";
    public int? ProcessId => Running ? process!.Id : null;
    public double MemoryGiB
    {
        get { if (!Running) return 0; process!.Refresh(); return process.WorkingSet64 / 1073741824d; }
    }
    public static List<string> Arguments(string exe, LaunchProfile p, string model, GgufInfo? metadata = null)
    {
        if (!File.Exists(exe)) throw new FileNotFoundException("请在路径设置中选择有效的 llama-server.exe。", exe);
        if (!File.Exists(model)) throw new FileNotFoundException("模型文件已不存在，请刷新模型列表。", model);
        ValidateProfile(p);
        List<string> args = ["--model", model, "--host", "127.0.0.1", "--port", p.Port.ToString(),
            "--ctx-size", p.Context.ToString(), "--n-gpu-layers", p.GpuLayers,
            "--parallel", "1", "--batch-size", p.Batch.ToString(), "--flash-attn", p.FlashAttention,
            "--cache-type-k", p.CacheK, "--cache-type-v", p.CacheV,
            "--temp", p.Temperature.ToString(CultureInfo.InvariantCulture), "--top-p", p.TopP.ToString(CultureInfo.InvariantCulture),
            "--top-k", p.TopK.ToString(), "--repeat-penalty", p.RepeatPenalty.ToString(CultureInfo.InvariantCulture),
            "--offline", "--log-colors", "off", "--alias", Path.GetFileNameWithoutExtension(model)];
        var kwargs = new Dictionary<string, object>();
        if (p.ThinkingMode != "auto" || p.ReasoningEffort != "default")
        {
            metadata ??= GgufInfo.Read(model);
            if (p.ThinkingMode != "auto")
            {
                if (!metadata.Thinking) throw new ArgumentException("此模型的聊天模板未声明 enable_thinking。");
                kwargs["enable_thinking"] = p.ThinkingMode == "on";
            }
            if (p.ReasoningEffort != "default")
            {
                if (!metadata.Reasoning) throw new ArgumentException("此模型的聊天模板未声明 reasoning_effort。");
                kwargs["reasoning_effort"] = p.ReasoningEffort;
            }
            args.AddRange(["--jinja", "--chat-template-kwargs", JsonSerializer.Serialize(kwargs)]);
        }
        if (p.Threads > 0) args.AddRange(["--threads", p.Threads.ToString()]);
        if (!string.IsNullOrEmpty(p.Projector))
        {
            if (!File.Exists(p.Projector)) throw new FileNotFoundException("视觉投影文件不存在。", p.Projector);
            args.AddRange(["--mmproj", p.Projector]);
        }
        return args;
    }
    public static void ValidateProfile(LaunchProfile p)
    {
        if (!new[] { "auto", "on", "off" }.Contains(p.ThinkingMode) || !new[] { "default", "low", "medium", "high" }.Contains(p.ReasoningEffort))
            throw new ArgumentException("思考模式或推理强度无效。");
        if (p.Port is < 1 or > 65535) throw new ArgumentException("端口必须在 1–65535 之间。");
        if (p.Context is < 512 or > 1048576) throw new ArgumentException("上下文长度必须在 512–1048576 之间。");
        if (p.Threads is < 0 or > 1024) throw new ArgumentException("CPU 线程数必须在 0–1024 之间。");
        if (p.Batch is < 1 or > 8192) throw new ArgumentException("批大小必须在 1–8192 之间。");
        if (p.GpuLayers != "auto" && p.GpuLayers != "all" && (!int.TryParse(p.GpuLayers, out int n) || n < 0))
            throw new ArgumentException("GPU 层数填写 auto、all 或非负整数；0 表示使用 CPU。");
        if (!new[] { "auto", "on", "off" }.Contains(p.FlashAttention)) throw new ArgumentException("Flash Attention 参数无效。");
        string[] cacheTypes = ["f16", "bf16", "q8_0", "q4_0", "q4_1", "q5_0", "q5_1", "iq4_nl"];
        if (!cacheTypes.Contains(p.CacheK) || !cacheTypes.Contains(p.CacheV)) throw new ArgumentException("KV 缓存类型无效。");
        if (!double.IsFinite(p.Temperature) || p.Temperature is < 0 or > 2 || !double.IsFinite(p.TopP) || p.TopP is < 0 or > 1
            || p.TopK < 0 || !double.IsFinite(p.RepeatPenalty) || p.RepeatPenalty is < 0 or > 3)
            throw new ArgumentException("采样参数超出允许范围。");
    }
    public static string Command(string exe, IEnumerable<string> args) => Quote(exe) + " " + string.Join(" ", args.Select(Quote));
    private static string Quote(string arg) => arg.Length == 0 || arg.Any(char.IsWhiteSpace) || arg.Contains('"')
        ? "\"" + arg.Replace("\"", "\\\"") + "\"" : arg;
    public async Task StartAsync(string exe, LaunchProfile profile, string model)
    {
        if (Running) throw new InvalidOperationException("请先停止正在运行的模型。");
        var args = Arguments(exe, profile, model);
        var probe = new TcpListener(IPAddress.Loopback, profile.Port);
        probe.Server.ExclusiveAddressUse = true;
        try { probe.Start(); }
        catch (SocketException) { throw new IOException($"端口 {profile.Port} 已被占用，请修改端口后重试。"); }
        finally { probe.Stop(); }
        await ReleaseProcessAsync();
        Directory.CreateDirectory(Path.Combine(Settings.DataRoot, "logs"));
        LogPath = Path.Combine(Settings.DataRoot, "logs", $"server-{DateTime.Now:yyyyMMdd-HHmmss-fff}.log");
        fileLog = new StreamWriter(LogPath, false, new UTF8Encoding(false)) { AutoFlush = true };
        var info = new ProcessStartInfo(exe)
        {
            WorkingDirectory = Path.GetDirectoryName(exe)!, UseShellExecute = false,
            CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true,
            StandardErrorEncoding = Encoding.UTF8, StandardOutputEncoding = Encoding.UTF8
        };
        foreach (var key in info.Environment.Keys.Where(k => k.StartsWith("LLAMA_ARG_", StringComparison.OrdinalIgnoreCase)).ToArray())
            info.Environment.Remove(key);
        foreach (string arg in args) info.ArgumentList.Add(arg);
        ModelPath = model; BaseUrl = $"http://127.0.0.1:{profile.Port}"; ActiveProfile = profile.Clone();
        Ready = false; State = "正在加载模型…"; FailureDetails = "";
        Log("启动：" + Command(exe, args));
        var child = new Process { StartInfo = info };
        try
        {
            job = new ProcessJob();
            if (!child.Start()) throw new IOException("无法启动 llama-server。");
            process = child; job.Assign(child);
            readers = [ReadAsync(child.StandardOutput), ReadAsync(child.StandardError)];
            monitor = new CancellationTokenSource();
            monitorTask = MonitorAsync(child, monitor.Token);
        }
        catch
        {
            if (process is { HasExited: false }) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
            child.Dispose(); process = null; job?.Dispose(); job = null;
            State = "启动失败"; fileLog?.Dispose(); fileLog = null;
            throw;
        }
    }
    private async Task ReadAsync(StreamReader reader)
    {
        while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            if (line.Contains("error", StringComparison.OrdinalIgnoreCase) || line.Contains("failed", StringComparison.OrdinalIgnoreCase)
                || line.Contains("out of memory", StringComparison.OrdinalIgnoreCase)) FailureDetails = line;
            Log(line);
        }
    }
    private async Task MonitorAsync(Process child, CancellationToken token)
    {
        using var http = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(3) };
        try
        {
            while (!token.IsCancellationRequested && !child.HasExited)
            {
                try
                {
                    using var response = await http.GetAsync(BaseUrl + "/health", token);
                    using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
                    bool healthy = response.IsSuccessStatusCode && body.RootElement.TryGetProperty("status", out var status) && status.GetString() == "ok";
                    if (healthy && !child.HasExited)
                    {
                        if (!Ready) Log("模型加载完成，服务已就绪：" + BaseUrl);
                        Ready = true; State = "服务已就绪";
                    }
                    else { Ready = false; State = "等待服务就绪…"; }
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
                { Ready = false; if (!token.IsCancellationRequested) State = "正在加载 / 等待服务响应…"; }
                await Task.Delay(1000, token);
            }
            if (!token.IsCancellationRequested)
            {
                await Task.WhenAll(readers);
                Ready = false; State = $"服务已退出（代码 {child.ExitCode}）";
                Log(State + "，请查看日志。");
            }
        }
        catch (OperationCanceledException) { }
    }
    public async Task StopAsync()
    {
        monitor?.Cancel(); Ready = false;
        if (process is { HasExited: false } child)
        {
            State = "正在停止…";
            try { child.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) when (child.HasExited) { }
            await child.WaitForExitAsync(); await Task.WhenAll(readers);
            Log("服务已停止。");
        }
        await ReleaseProcessAsync(); State = "已停止";
    }
    private async Task ReleaseProcessAsync()
    {
        monitor?.Cancel(); await monitorTask; monitorTask = Task.CompletedTask;
        monitor?.Dispose(); monitor = null;
        if (process is not null)
        {
            if (!process.HasExited) throw new InvalidOperationException("服务仍在运行。");
            await Task.WhenAll(readers); process.Dispose(); process = null;
        }
        job?.Dispose(); job = null;
        lock (logLock) { fileLog?.Dispose(); fileLog = null; }
    }
    public void Log(string message)
    {
        lock (logLock)
        {
            string entry = $"[{DateTime.Now:HH:mm:ss}] {message}";
            Messages.Enqueue(entry);
            if (Messages.Count > 6000) Messages.TryDequeue(out _);
            try { fileLog?.WriteLine(entry); }
            catch (IOException ex) { Messages.Enqueue("日志写入失败：" + ex.Message); fileLog?.Dispose(); fileLog = null; }
        }
    }
    public async ValueTask DisposeAsync() => await StopAsync();
}
