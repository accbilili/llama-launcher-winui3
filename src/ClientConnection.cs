using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LlamaLauncher;

internal enum LocalClient { WorkBuddy, CherryStudio, DeepSeekHarness }

internal sealed record ClientConnection(string Root, string ModelId, int Context, bool Tools, bool Images, bool Reasoning)
{
    public string ApiBase => Root.TrimEnd('/') + "/v1";
    public string ChatEndpoint => ApiBase + "/chat/completions";
    public string ProviderId => "llama-launcher-" + new Uri(Root).Port;
    public int MaxOutput => Math.Min(4096, Context / 4);
    public string Address(LocalClient client) => client == LocalClient.WorkBuddy ? ChatEndpoint : ApiBase;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public JsonObject WorkBuddyModel() => new()
    {
        ["id"] = ModelId, ["name"] = "本地 · " + ModelId, ["vendor"] = "Custom",
        ["url"] = ChatEndpoint, ["apiKey"] = "local", ["useCustomProtocol"] = true,
        ["maxInputTokens"] = Context - MaxOutput, ["maxOutputTokens"] = MaxOutput,
        ["supportsToolCall"] = Tools, ["supportsImages"] = Images, ["supportsReasoning"] = Reasoning
    };
    public string WorkBuddyJson() => new JsonArray(WorkBuddyModel()).ToJsonString(JsonOptions);

    public string CherryImportUri()
    {
        string json = JsonSerializer.Serialize(new
        { id = ProviderId, name = "本地 llama-server · " + new Uri(Root).Port, type = "openai", baseUrl = ApiBase, apiKey = "local" });
        // Cherry Studio v2 的导入协议使用 _ 表示 +、- 表示 /。
        string data = Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).Replace('+', '_').Replace('/', '-');
        return "cherrystudio://providers/api-keys?v=1&data=" + Uri.EscapeDataString(data);
    }
    public string DshYaml() => $"""
        # 将 providers 下的条目合并到当前 profile 的 llm-pi-ai 配置。
        # 保留已有其他 provider；凭据在 Harness 设置中填写 local。
        - id: llm-pi-ai
          config:
            providers:
              {ProviderId}:
                displayName: "本地 llama-server"
                api: openai-completions
                baseURL: {JsonSerializer.Serialize(ApiBase)}
                compat:
                  supportsDeveloperRole: false
                  maxTokensField: max_tokens
                models:
                  - id: {JsonSerializer.Serialize(ModelId)}
                    name: {JsonSerializer.Serialize(ModelId)}
                    contextWindow: {Context}
                    maxTokens: {MaxOutput}
                    input: [{(Images ? "text, image" : "text")}]
        """;

    public string Instructions(LocalClient client) => client switch
    {
        LocalClient.WorkBuddy => $"""
            设置 → 模型 → 添加 / 编辑 → 自定义（Custom）
            接口 URL：{ChatEndpoint}
            模型 ID：{ModelId}
            API Key：local
            自定义协议：开启
            工具调用：{Yes(Tools)}；图片输入：{Yes(Images)}；推理模式：{Yes(Reasoning)}
            最大输入：{Context - MaxOutput}；最大输出：{MaxOutput}

            “写入 WorkBuddy”会在 ~/.workbuddy/models.json 中添加或更新同 ID 的本地模型，保留其他项并先创建备份。保存后在 WorkBuddy 重新选择模型；列表未刷新时重新打开 WorkBuddy。
            """,
        LocalClient.CherryStudio => $"""
            设置 → 模型服务 → 添加服务商 → OpenAI / OpenAI 兼容
            API 地址：{ApiBase}
            API Key：local
            模型 ID：{ModelId}
            请求协议：Chat Completions（不要选 Responses 或 Ollama）

            点击“导入 Cherry Studio”后，在 Cherry Studio 的确认框中确认服务商。导入只设置服务商地址和占位密钥；随后点击管理 / 获取模型，添加上面的模型 ID。服务商按端口命名，同一端口切换模型后需要刷新模型列表。
            """,
        _ => $"""
            设置 → 模型 → 添加模型提供商 → 自定义模型 API
            Provider ID：{ProviderId}
            显示名称：本地 llama-server
            基础 URL：{ApiBase}
            API 协议：OpenAI Chat Completions（openai-completions）
            API Key：local
            模型 ID：{ModelId}
            上下文窗口：{Context}；最大输出：{MaxOutput}
            输入类型：{(Images ? "文本、图片" : "文本")}

            点击“获取可用模型”并添加上面的模型，再保存。选择此提供商创建新会话；旧会话可能继续使用原模型。
            思考先使用服务端默认值，需要时在启动参数中调整后重启模型。
            如需手动编辑，可复制配置片段，将 provider 合并到当前 profile 的 llm-pi-ai 配置；不要用片段替换整个配置文件。凭据仍在 Harness 设置中填写 local。
            """
    };
    private static string Yes(bool value) => value ? "开启" : "关闭";

    public static string WorkBuddyPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".workbuddy", "models.json");
    public string SaveWorkBuddy(string path)
    {
        byte[]? original = File.Exists(path) ? File.ReadAllBytes(path) : null;
        var array = original is null ? new JsonArray() : ParseArray(original)
            ?? throw new InvalidDataException("WorkBuddy models.json 应为数组；未改动原文件。");
        var matches = array.OfType<JsonObject>().Where(m => m["id"]?.GetValue<string>() == ModelId).ToList();
        if (matches.Count > 1) throw new InvalidDataException("WorkBuddy 中有多个相同模型 ID，请在客户端中整理后重试。");
        var target = matches.SingleOrDefault();
        if (target is not null && (!Uri.TryCreate(target["url"]?.GetValue<string>(), UriKind.Absolute, out var uri) || !uri.IsLoopback))
            throw new InvalidOperationException("WorkBuddy 已有同 ID 的非本地模型，请先在 WorkBuddy 中调整该项的模型 ID。");
        var entry = WorkBuddyModel();
        if (target is null) array.Add(entry);
        else foreach (var field in entry) target[field.Key] = field.Value?.DeepClone();
        return WriteWithBackup(path, Encoding.UTF8.GetBytes(array.ToJsonString(JsonOptions)), original);
    }
    public static FileInfo[] WorkBuddyBackups(string path)
    {
        string folder = Path.GetDirectoryName(Path.GetFullPath(path))!;
        return Directory.Exists(folder) ? Directory.EnumerateFiles(folder, Path.GetFileName(path) + ".backup-*")
            .Select(p => new FileInfo(p)).OrderByDescending(f => f.Name, StringComparer.Ordinal).ToArray() : [];
    }
    public static string RestoreWorkBuddy(string path, string backup)
    {
        string folder = Path.GetDirectoryName(Path.GetFullPath(path))!;
        string backupPath = Path.GetFullPath(backup);
        if (!string.Equals(Path.GetDirectoryName(backupPath), folder, StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileName(backupPath).StartsWith(Path.GetFileName(path) + ".backup-", StringComparison.Ordinal))
            throw new ArgumentException("请选择此 WorkBuddy 配置目录中的原备份文件。");
        byte[] bytes = File.ReadAllBytes(backupPath);
        if (ParseArray(bytes) is null) throw new InvalidDataException("备份不是有效的 WorkBuddy 模型列表，未恢复。");
        return WriteWithBackup(path, bytes, File.Exists(path) ? File.ReadAllBytes(path) : null);
    }
    private static JsonArray? ParseArray(byte[] bytes)
    {
        using var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return JsonNode.Parse(reader.ReadToEnd()) as JsonArray;
    }
    private static string WriteWithBackup(string path, byte[] bytes, byte[]? original)
    {
        string folder = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(folder);
        string backup = "";
        if (original is not null)
        {
            backup = path + ".backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N")[..6];
            File.Copy(path, backup, false);
        }
        string temporary = Path.Combine(folder, "models-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllBytes(temporary, bytes);
            if (original is not null && !File.ReadAllBytes(path).SequenceEqual(original))
                throw new IOException("WorkBuddy 配置刚被其他程序修改，请重试；未覆盖新内容。");
            if (original is null && File.Exists(path)) throw new IOException("WorkBuddy 已创建配置，请重试。");
            File.Move(temporary, path, original is not null);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return backup;
    }
}
