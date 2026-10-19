using LlamaLauncher;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;

internal static class DiagnosticChecks
{
    public static async Task Run()
    {
        var c = new ClientConnection("http://127.0.0.1:18081", "test-model", 8192, true, false, true);
        var handler = new FakeHandler();
        using var http = new HttpClient(handler);
        await ClientDiagnostics.RunAsync(http, c, DiagnosticMode.Service, default);
        Check(handler.Posts == 0, "健康检查不生成");
        string basic = await ClientDiagnostics.RunAsync(http, c, DiagnosticMode.Basic, default);
        Check(basic.Contains("OK") && handler.Posts == 1, "基础回复");
        string stream = await ClientDiagnostics.RunAsync(http, c, DiagnosticMode.Streaming, default);
        Check(stream.Contains("2 个片段") && stream.Contains("OK"), "完整 SSE 流");
        string tools = await ClientDiagnostics.RunAsync(http, c, DiagnosticMode.Tools, default);
        Check(tools.Contains("结果回传均正确") && handler.ToolResults == 1, "工具调用与回传");
        handler.BrokenStream = true;
        await Expect<IOException>(() => ClientDiagnostics.RunAsync(http, c, DiagnosticMode.Streaming, default), "拒绝中断 SSE");
        handler.BrokenStream = false; handler.InvalidArguments = true;
        await Expect<System.Text.Json.JsonException>(() => ClientDiagnostics.RunAsync(http, c, DiagnosticMode.Tools, default), "拒绝损坏工具参数");
        handler.InvalidArguments = false; handler.WrongModel = true;
        int before = handler.Posts;
        await Expect<IOException>(() => ClientDiagnostics.RunAsync(http, c, DiagnosticMode.Basic, default), "模型不符时拒绝生成");
        Check(before == handler.Posts, "模型不符无生成请求");
        handler.WrongModel = false; handler.Failure = HttpStatusCode.NotFound;
        try { await ClientDiagnostics.RunAsync(http, c, DiagnosticMode.Basic, default); throw new Exception("应拒绝 404"); }
        catch (HttpRequestException ex) { Check(ex.StatusCode == HttpStatusCode.NotFound && Errors.Explain(ex).Contains("接口路径"), "保留 HTTP 状态和处理提示"); }
        handler.Failure = null;
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await Expect<OperationCanceledException>(() => ClientDiagnostics.RunAsync(http, c, DiagnosticMode.Basic, canceled.Token), "取消请求");
        Console.WriteLine("PASS: 模拟 HTTP 检查基础回复、完整/中断 SSE、工具调用与回传、损坏参数、模型不符、404 和取消。无真实网络请求。");
    }
    private static void Check(bool condition, string name) { if (!condition) throw new Exception("诊断检查失败：" + name); }
    private static async Task Expect<T>(Func<Task> task, string name) where T : Exception
    { try { await task(); } catch (T) { return; } throw new Exception("诊断检查失败：" + name); }
    private sealed class FakeHandler : HttpMessageHandler
    {
        public int Posts, ToolResults;
        public bool BrokenStream, InvalidArguments, WrongModel;
        public HttpStatusCode? Failure;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Check(request.RequestUri?.Host == "127.0.0.1" && request.RequestUri.Port == 18081, "本地目标");
            if (Failure is { } status) return new(status) { Content = new StringContent("test failure") };
            string path = request.RequestUri!.AbsolutePath;
            if (path == "/health") return Json("{\"status\":\"ok\"}");
            if (path == "/v1/models") return Json("{\"data\":[{\"id\":\"" + (WrongModel ? "other" : "test-model") + "\"}]}");
            Check(path == "/v1/chat/completions" && request.Method == HttpMethod.Post, "完整聊天接口");
            Posts++;
            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(token))!.AsObject();
            Check(body["model"]!.GetValue<string>() == "test-model" && body["max_tokens"]!.GetValue<int>() == 2048, "模型 ID 与预算");
            if (body["stream"]!.GetValue<bool>())
            {
                string sse = "data: {\"choices\":[{\"delta\":{\"content\":\"O\"}}]}\n\ndata: {\"choices\":[{\"delta\":{\"content\":\"K\"}}]}\n\n";
                if (!BrokenStream) sse += "data: {\"choices\":[{\"delta\":{},\"finish_reason\":\"stop\"}]}\n\ndata: [DONE]\n\n";
                return new(HttpStatusCode.OK) { Content = new StringContent(sse, Encoding.UTF8, "text/event-stream") };
            }
            if (body["tool_choice"] is JsonObject)
            {
                string arguments = InvalidArguments ? "{broken" : "{\"text\":\"ping\"}";
                return Json(new JsonObject { ["choices"] = new JsonArray(new JsonObject { ["message"] = new JsonObject
                {
                    ["role"] = "assistant", ["content"] = null, ["tool_calls"] = new JsonArray(new JsonObject { ["id"] = "call-1", ["type"] = "function",
                        ["function"] = new JsonObject { ["name"] = "launcher_echo", ["arguments"] = arguments } })
                } }) }.ToJsonString());
            }
            if (body["tool_choice"]?.GetValue<string>() == "none")
            {
                Check(body["messages"]![2]!["role"]!.GetValue<string>() == "tool" && body["messages"]![2]!["content"]!.GetValue<string>() == "ping", "工具回传消息");
                ToolResults++;
            }
            return Json("{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"OK\"}}]}");
        }
        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
