using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LlamaLauncher;

internal enum DiagnosticMode { Service, Basic, Streaming, Tools }

internal static class ClientDiagnostics
{
    public static async Task<string> RunAsync(HttpClient http, ClientConnection connection, DiagnosticMode mode, CancellationToken token)
    {
        using (var health = await http.GetAsync(connection.Root + "/health", token))
        {
            await EnsureSuccess(health, token);
            using var body = JsonDocument.Parse(await health.Content.ReadAsStringAsync(token));
            if (!body.RootElement.TryGetProperty("status", out var status) || status.GetString() != "ok") throw new IOException("模型服务尚未返回就绪状态，请等待加载完成。");
        }
        using (var models = await http.GetAsync(connection.ApiBase + "/models", token))
        {
            await EnsureSuccess(models, token);
            using var body = JsonDocument.Parse(await models.Content.ReadAsStringAsync(token));
            if (!body.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) throw new IOException("模型列表格式不符合 OpenAI 接口，请检查端口和服务类型。");
            var ids = data.EnumerateArray().Select(m => m.GetProperty("id").GetString() ?? "").ToArray();
            if (!ids.Contains(connection.ModelId, StringComparer.Ordinal)) throw new IOException("模型 ID 不一致。服务实际提供：" + string.Join("、", ids) + "。请重新选择正确的模型 ID。");
        }
        if (mode == DiagnosticMode.Service) return "连接检查通过：服务已就绪，模型 ID 正确。生成、流式和工具调用尚未检测。";
        var messages = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = mode == DiagnosticMode.Tools
            ? "请调用 launcher_echo 工具，将 text 设置为 ping。不要执行其他操作。" : "请只回复 OK。" });
        var request = new JsonObject { ["model"] = connection.ModelId, ["messages"] = messages, ["max_tokens"] = connection.MaxOutput, ["stream"] = mode == DiagnosticMode.Streaming };
        if (mode == DiagnosticMode.Streaming) return await Streaming(http, connection.ChatEndpoint, request, token);
        if (mode == DiagnosticMode.Tools)
        {
            request["tools"] = new JsonArray(new JsonObject
            {
                ["type"] = "function", ["function"] = new JsonObject
                {
                    ["name"] = "launcher_echo", ["description"] = "无副作用的测试回显函数，只返回收到的 text。",
                    ["parameters"] = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject
                        { ["text"] = new JsonObject { ["type"] = "string" } }, ["required"] = new JsonArray("text"), ["additionalProperties"] = false }
                }
            });
            request["tool_choice"] = new JsonObject { ["type"] = "function", ["function"] = new JsonObject { ["name"] = "launcher_echo" } };
        }
        var message = await Completion(http, connection.ChatEndpoint, request, token);
        if (mode == DiagnosticMode.Basic) return "基础回复测试通过。\n模型回复：" + GetText(message);
        if (message["tool_calls"] is not JsonArray calls || calls.Count != 1 || calls[0] is not JsonObject call
            || call["function"]?["name"]?.GetValue<string>() != "launcher_echo" || string.IsNullOrEmpty(call["id"]?.GetValue<string>()))
            throw new IOException("未收到预期的工具调用。模型可能不支持工具接口，或输出预算已用于思考。请调整参数后重试。");
        using var args = JsonDocument.Parse(call["function"]!["arguments"]!.GetValue<string>());
        if (!args.RootElement.TryGetProperty("text", out var value) || value.GetString() != "ping") throw new IOException("工具参数未通过检查，应返回有效 JSON 且 text 为 ping。");
        messages.Add(message.DeepClone());
        messages.Add(new JsonObject { ["role"] = "tool", ["tool_call_id"] = call["id"]!.GetValue<string>(), ["content"] = "ping" });
        request["tool_choice"] = "none";
        var followup = await Completion(http, connection.ChatEndpoint, request, token);
        return "工具测试通过：指定工具调用、JSON 参数和结果回传均正确。未执行任何外部工具。\n回传后的回复：" + GetText(followup);
    }
    private static async Task<JsonObject> Completion(HttpClient http, string url, JsonObject request, CancellationToken token)
    {
        using var response = await http.PostAsJsonAsync(url, request, token); await EnsureSuccess(response, token);
        var root = JsonNode.Parse(await response.Content.ReadAsStringAsync(token));
        if (root?["choices"] is not JsonArray { Count: > 0 } choices || choices[0]?["message"] is not JsonObject message)
            throw new IOException("服务未返回有效的 Chat Completions 消息，请检查协议类型。");
        return message.DeepClone().AsObject();
    }
    private static string GetText(JsonObject message)
    {
        string text = message["content"]?.GetValue<string>() ?? "";
        if (string.IsNullOrWhiteSpace(text)) throw new IOException("服务返回成功状态，但正文为空。可能输出预算已被思考用尽，请增大预算或关闭思考后重试。");
        return text.Length > 1000 ? text[..1000] + "…" : text;
    }
    private static async Task<string> Streaming(HttpClient http, string url, JsonObject body, CancellationToken token)
    {
        body["stream_options"] = new JsonObject { ["include_usage"] = true };
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token); await EnsureSuccess(response, token);
        if (!string.Equals(response.Content.Headers.ContentType?.MediaType, "text/event-stream", StringComparison.OrdinalIgnoreCase))
            throw new IOException("流式请求未返回 text/event-stream，不能判定流式接口通过。");
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(token));
        var text = new StringBuilder(); int chunks = 0, thoughts = 0; bool done = false, finished = false;
        while (await reader.ReadLineAsync(token) is { } line)
        {
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
            string data = line[5..].Trim(); if (data.Length == 0) continue;
            if (data == "[DONE]") { done = true; break; }
            using var json = JsonDocument.Parse(data);
            if (json.RootElement.TryGetProperty("error", out var error)) throw new IOException(error.ToString());
            if (!json.RootElement.TryGetProperty("choices", out var choices)) continue;
            foreach (var choice in choices.EnumerateArray())
            {
                if (choice.TryGetProperty("finish_reason", out var finish) && finish.ValueKind == JsonValueKind.String) finished = true;
                if (!choice.TryGetProperty("delta", out var delta)) continue;
                if (delta.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String && content.GetString() is { Length: > 0 } fragment)
                { chunks++; if (text.Length < 1000) text.Append(fragment); }
                if (delta.TryGetProperty("reasoning_content", out var reason) && reason.ValueKind == JsonValueKind.String) thoughts += reason.GetString()?.Length ?? 0;
            }
        }
        if (!done || !finished || chunks == 0) throw new IOException("流式响应不完整或没有正文。已收到正文片段 " + chunks + "、思考字符 " + thoughts + "。请检查输出预算和服务日志。");
        return $"流式测试通过：正文 {chunks} 个片段，收到结束原因和 [DONE] 标记。\n模型回复：{text}";
    }
    private static async Task EnsureSuccess(HttpResponseMessage response, CancellationToken token)
    {
        if (response.IsSuccessStatusCode) return;
        string body = await response.Content.ReadAsStringAsync(token);
        throw new HttpRequestException($"HTTP {(int)response.StatusCode}：" + (body.Length > 500 ? body[..500] : body), null, response.StatusCode);
    }
}
