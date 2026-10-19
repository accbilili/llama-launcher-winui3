using LlamaLauncher;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text;

// 只读模型头、构造命令并检查数据布局；不启动客户端/服务，不发送 HTTP 请求。
var settings = Settings.Load(out string warning);
if (warning.Length > 0) throw new Exception(warning);
var models = ModelFile.Scan(settings.ModelRoot);
if (models.Count == 0) throw new Exception("未找到本地模型。");
foreach (var model in models)
{
    var info = model.Info ?? throw new Exception(model.InfoError);
    var p = new LaunchProfile();
    var argsList = Server.Arguments(settings.ServerPath, p, model.Path);
    Assert(argsList[argsList.IndexOf("--alias") + 1] == model.Name, "模型 ID");
    if (info.Thinking)
    {
        p.ThinkingMode = "off";
        var argsOff = Server.Arguments(settings.ServerPath, p, model.Path);
        using var kwargs = JsonDocument.Parse(argsOff[argsOff.IndexOf("--chat-template-kwargs") + 1]);
        Assert(!kwargs.RootElement.GetProperty("enable_thinking").GetBoolean(), "思考开关");
    }
    if (info.Reasoning)
    {
        p.ReasoningEffort = "low";
        Assert(Server.Arguments(settings.ServerPath, p, model.Path).Contains("--chat-template-kwargs"), "推理强度");
    }
    var copy = p.Clone(); copy.Port++;
    Assert(copy.Port != p.Port, "预设独立副本");
    Console.WriteLine($"{model.Name}: {info.Architecture}, {info.Quantization}, ctx={info.Context}, thinking={info.Thinking}, reasoning={info.Reasoning}");
}
var presetSettings = new Settings();
presetSettings.Presets[models[0].Path] = new() { ["检查预设"] = new() { ThinkingMode = "off", Context = 4096 } };
var roundTrip = JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(presetSettings))!;
Assert(roundTrip.Presets[models[0].Path]["检查预设"].Context == 4096, "预设序列化");
CheckLayout(typeof(GpuMonitor), "Description", 312);
CheckLayout(typeof(GpuMonitor), "CounterValue", 16);
CheckLayout(typeof(GpuMonitor), "CounterItem", 24);
CheckLayout(typeof(TrayIcon), "NotifyData", 976);
CheckClients();
await DiagnosticChecks.Run();
var baseParameters = new LaunchProfile(); var browserOnly = baseParameters.Clone(); browserOnly.OpenBrowser = true;
Assert(baseParameters.SameServerParameters(browserOnly), "自动打开浏览器不要求重启服务");
browserOnly.Context *= 2; Assert(!baseParameters.SameServerParameters(browserOnly), "上下文变化标记为尚未生效");
Console.WriteLine("PASS: GGUF、启动参数、预设数据和 x64 原生结构布局静态检查。未启动任何模型。");
static void Assert(bool condition, string name) { if (!condition) throw new Exception("检查失败：" + name); }
static void CheckLayout(Type owner, string name, int size)
{ Assert(Marshal.SizeOf(owner.GetNestedType(name, BindingFlags.NonPublic)!) == size, name + " 布局"); }

static void CheckClients()
{
    var connection = new ClientConnection("http://127.0.0.1:18081", "gpt-oss-20b-MXFP4", 8192, true, false, true);
    Assert(connection.Address(LocalClient.WorkBuddy) == "http://127.0.0.1:18081/v1/chat/completions", "WorkBuddy 完整端点");
    Assert(connection.Address(LocalClient.CherryStudio) == "http://127.0.0.1:18081/v1", "Cherry 基础地址");
    Assert(connection.Address(LocalClient.DeepSeekHarness) == connection.ApiBase, "Harness 基础地址");
    var deeplink = new Uri(connection.CherryImportUri());
    Assert(deeplink.Scheme == "cherrystudio" && deeplink.Host == "providers" && deeplink.AbsolutePath == "/api-keys", "Cherry 导入协议");
    string data = Uri.UnescapeDataString(deeplink.Query.Split("data=", 2)[1]).Replace('_', '+').Replace('-', '/');
    using var imported = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(data)));
    Assert(imported.RootElement.GetProperty("baseUrl").GetString() == connection.ApiBase, "Cherry 导入地址");
    Assert(imported.RootElement.GetProperty("type").GetString() == "openai", "Cherry Chat Completions 类型");
    Assert(!imported.RootElement.TryGetProperty("models", out _), "Cherry 不伪造协议不支持的模型导入字段");
    var folder = Path.Combine(Path.GetTempPath(), "LlamaLauncher-client-check-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(folder);
    string path = Path.Combine(folder, "models.json");
    try
    {
        Assert(connection.SaveWorkBuddy(path) == "", "新配置无原文件备份");
        var first = JsonNode.Parse(File.ReadAllText(path))!.AsArray();
        Assert(first.Count == 1 && first[0]!["id"]!.GetValue<string>() == connection.ModelId, "WorkBuddy 新建模型");
        first.Insert(0, new JsonObject { ["id"] = "existing-cloud-model", ["url"] = "https://example.com/v1/chat/completions", ["customField"] = 42 });
        first[1]!["keepMe"] = "untouched";
        File.WriteAllText(path, first.ToJsonString());
        byte[] before = File.ReadAllBytes(path);
        string backup = connection.SaveWorkBuddy(path);
        Assert(File.ReadAllBytes(backup).SequenceEqual(before), "WorkBuddy 原文件字节备份");
        var result = JsonNode.Parse(File.ReadAllText(path))!.AsArray();
        Assert(result.Count == 2 && result[0]!["customField"]!.GetValue<int>() == 42, "保留其他模型");
        Assert(result[1]!["keepMe"]!.GetValue<string>() == "untouched", "保留未知字段");
        Assert(result[1]!["useCustomProtocol"]!.GetValue<bool>() && result[1]!["url"]!.GetValue<string>() == connection.ChatEndpoint, "WorkBuddy 路径和协议配套");
        Assert(result[1]!["maxInputTokens"]!.GetValue<int>() + result[1]!["maxOutputTokens"]!.GetValue<int>() == connection.Context, "客户端预算使用实际上下文");
        byte[] currentBytes = File.ReadAllBytes(path);
        string beforeRestore = ClientConnection.RestoreWorkBuddy(path, backup);
        Assert(File.ReadAllBytes(path).SequenceEqual(before), "恢复备份保持原始字节");
        Assert(File.ReadAllBytes(beforeRestore).SequenceEqual(currentBytes), "恢复前再次备份当前配置");
        Assert(ClientConnection.WorkBuddyBackups(path).Length >= 2, "列出配置备份");
        string invalidBackup = path + ".backup-invalid"; File.WriteAllText(invalidBackup, "{broken");
        Expect<JsonException>(() => ClientConnection.RestoreWorkBuddy(path, invalidBackup), "损坏备份拒绝恢复");
        Assert(File.ReadAllBytes(path).SequenceEqual(before), "备份恢复失败不改动当前配置");
        Expect<ArgumentException>(() => ClientConnection.RestoreWorkBuddy(path, Path.Combine(folder, "unrelated.json")), "拒绝非本配置的备份路径");
        result[1]!["url"] = "https://example.com/v1/chat/completions";
        File.WriteAllText(path, result.ToJsonString());
        string remote = File.ReadAllText(path);
        Expect<InvalidOperationException>(() => connection.SaveWorkBuddy(path), "拒绝覆盖同 ID 非本地配置");
        Assert(File.ReadAllText(path) == remote, "拒绝时保持原文件");
        File.WriteAllText(path, "{invalid json");
        Expect<JsonException>(() => connection.SaveWorkBuddy(path), "损坏 JSON 不覆盖");
        Assert(File.ReadAllText(path) == "{invalid json", "保留损坏原文件");
        File.WriteAllText(path, "{}");
        Expect<InvalidDataException>(() => connection.SaveWorkBuddy(path), "不接受非数组结构");
    }
    finally
    {
        foreach (string file in Directory.GetFiles(folder)) File.Delete(file);
        Directory.Delete(folder);
    }
    string evidence = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "build-artifacts", "client-checks"));
    Directory.CreateDirectory(evidence);
    File.WriteAllText(Path.Combine(evidence, "workbuddy.json"), connection.WorkBuddyJson());
    File.WriteAllText(Path.Combine(evidence, "cherry-import.txt"), connection.CherryImportUri());
    File.WriteAllText(Path.Combine(evidence, "deepseek-harness.yml"), connection.DshYaml());
    Console.WriteLine("PASS: 三客户端地址、Cherry 导入链接解码、WorkBuddy 增改/备份/字段保留/拒绝损坏与冲突配置。仅使用临时测试文件。");
}
static void Expect<T>(Action action, string name) where T : Exception
{
    try { action(); }
    catch (T) { return; }
    throw new Exception("检查失败：" + name);
}
