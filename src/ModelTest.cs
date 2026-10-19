using System.Diagnostics;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace LlamaLauncher;

internal sealed record TestUpdate(string Text, string Reasoning, string Statistics);

internal static class ModelTest
{
    public static async Task RunAsync(string baseUrl, string model, string prompt, int maxTokens, IProgress<TestUpdate> progress, CancellationToken token)
    {
        using var http = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = Timeout.InfiniteTimeSpan };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        var watch = Stopwatch.StartNew();
        var text = new StringBuilder(); var reasoning = new StringBuilder();
        double? first = null, speed = null; int? tokens = null, input = null; string finish = "";
        long lastReport = -100;
        void Report(string state)
        {
            string stats = $"{state}  ·  总耗时 {watch.Elapsed.TotalSeconds:0.00} 秒  ·  首个生成片段 {(first.HasValue ? first.Value.ToString("0.00") + " 秒" : "等待中")}";
            if (input.HasValue) stats += $"  ·  输入 {input} tokens";
            if (tokens.HasValue) stats += $"  ·  输出 {tokens} tokens（含思考）";
            stats += speed.HasValue ? $"  ·  服务端生成速度 {speed:0.0} tokens/秒" : "  ·  速度未返回";
            if (finish == "length") stats += "  ·  已达到输出上限，可增大上限后重试";
            progress.Report(new(text.ToString(), reasoning.ToString(), stats));
        }
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, baseUrl + "/v1/chat/completions")
            {
                Content = JsonContent.Create(new { model, messages = new[] { new { role = "user", content = prompt } }, max_tokens = maxTokens,
                    stream = true, stream_options = new { include_usage = true }, timings_per_token = true })
            };
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode) throw new HttpRequestException($"模型返回 HTTP {(int)response.StatusCode}：" + await response.Content.ReadAsStringAsync(timeout.Token), null, response.StatusCode);
            using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(timeout.Token));
            bool done = false;
            while (await reader.ReadLineAsync(timeout.Token) is { } line)
            {
                if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
                string data = line[5..].Trim();
                if (data == "[DONE]") { done = true; break; }
                if (data.Length == 0) continue;
                using var json = JsonDocument.Parse(data); var root = json.RootElement;
                if (root.TryGetProperty("error", out var error)) throw new IOException(error.ToString());
                if (root.TryGetProperty("choices", out var choices))
                    foreach (var choice in choices.EnumerateArray())
                    {
                        if (choice.TryGetProperty("finish_reason", out var reason) && reason.ValueKind == JsonValueKind.String) finish = reason.GetString()!;
                        if (!choice.TryGetProperty("delta", out var delta)) continue;
                        if (delta.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String) text.Append(content.GetString());
                        if (delta.TryGetProperty("reasoning_content", out var thought) && thought.ValueKind == JsonValueKind.String) reasoning.Append(thought.GetString());
                    }
                if (!first.HasValue && (text.Length > 0 || reasoning.Length > 0)) first = watch.Elapsed.TotalSeconds;
                if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
                {
                    if (usage.TryGetProperty("completion_tokens", out var outputCount)) tokens = outputCount.GetInt32();
                    if (usage.TryGetProperty("prompt_tokens", out var inputCount)) input = inputCount.GetInt32();
                }
                if (root.TryGetProperty("timings", out var timings) && timings.ValueKind == JsonValueKind.Object)
                {
                    if (timings.TryGetProperty("predicted_per_second", out var rate)) speed = rate.GetDouble();
                    if (timings.TryGetProperty("predicted_n", out var predicted)) tokens = predicted.GetInt32();
                }
                if (watch.ElapsedMilliseconds - lastReport >= 100) { Report("生成中"); lastReport = watch.ElapsedMilliseconds; }
            }
            if (!done && finish.Length == 0) throw new IOException("响应在完成标记之前中断；已收到的内容保留在下方。");
            watch.Stop(); Report("已完成");
        }
        catch (OperationCanceledException)
        {
            watch.Stop(); Report(token.IsCancellationRequested ? "已取消" : "已超时（5 分钟）");
        }
        catch
        {
            watch.Stop(); Report("失败，保留已收到的内容"); throw;
        }
    }
}
