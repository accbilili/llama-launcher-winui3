using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace LlamaLauncher;

internal static class Errors
{
    public static string Explain(Exception ex)
    {
        if (ex is HttpRequestException http)
        {
            string? hint = http.StatusCode switch
            {
                HttpStatusCode.NotFound => "接口路径不存在（404）。请核对地址：WorkBuddy 自定义协议需使用完整 /v1/chat/completions，其余客户端使用 /v1。",
                HttpStatusCode.Unauthorized => "密钥未通过验证（401）。检查客户端密钥是否与服务端一致。",
                HttpStatusCode.Forbidden => "服务拒绝访问（403）。检查服务端权限和访问设置。",
                HttpStatusCode.BadRequest => "服务拒绝了请求参数（400）。检查上下文、输出上限和所选模型支持的接口参数。",
                HttpStatusCode.ServiceUnavailable => "服务暂不可用（503），可能仍在加载或忙碌。等待模型就绪后重试。",
                _ => null
            };
            if (hint is not null) return hint + "\n详情：" + Short(ex.Message);
            if (http.GetBaseException() is SocketException) return "无法连接本地服务。请先启动模型，核对端口，再点击检查连接。\n详情：" + Short(ex.Message);
        }
        if (ex is OperationCanceledException) return "请求已取消或等待超时。请检查服务状态后重试。";
        if (ex is JsonException) return "JSON 格式无法解析。请检查配置文件是否完整，以及服务是否返回正确的接口格式。\n详情：" + Short(ex.Message);
        string text = ex.Message;
        if (text.Contains("out of memory", StringComparison.OrdinalIgnoreCase) || text.Contains("failed to allocate", StringComparison.OrdinalIgnoreCase)
            || text.Contains("out_of_device_memory", StringComparison.OrdinalIgnoreCase))
            return "模型加载或生成时内存不足。请降低上下文、减少 GPU 层数，或使用低显存预设后重试。\n详情：" + Short(text);
        return Short(text);
    }
    private static string Short(string text) => text.Length > 550 ? text[..550] + "…（完整信息见日志）" : text;
}
