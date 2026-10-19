using System.Text.Json;
using System.Text.RegularExpressions;

namespace LlamaLauncher;

internal sealed class LaunchProfile
{
    public int Port { get; set; } = 8080;
    public int Context { get; set; } = 8192;
    public string GpuLayers { get; set; } = "auto";
    public int Threads { get; set; }
    public int Batch { get; set; } = 2048;
    public string FlashAttention { get; set; } = "auto";
    public string CacheK { get; set; } = "f16";
    public string CacheV { get; set; } = "f16";
    public double Temperature { get; set; } = 0.8;
    public double TopP { get; set; } = 0.95;
    public int TopK { get; set; } = 40;
    public double RepeatPenalty { get; set; } = 1.0;
    public string Projector { get; set; } = "";
    public bool OpenBrowser { get; set; }
    public string ThinkingMode { get; set; } = "auto";
    public string ReasoningEffort { get; set; } = "default";
    public LaunchProfile Clone() => (LaunchProfile)MemberwiseClone();
    public bool SameServerParameters(LaunchProfile p) => Port == p.Port && Context == p.Context && GpuLayers == p.GpuLayers
        && Threads == p.Threads && Batch == p.Batch && FlashAttention == p.FlashAttention && CacheK == p.CacheK && CacheV == p.CacheV
        && Temperature == p.Temperature && TopP == p.TopP && TopK == p.TopK && RepeatPenalty == p.RepeatPenalty
        && Projector == p.Projector && ThinkingMode == p.ThinkingMode && ReasoningEffort == p.ReasoningEffort;
}

internal sealed class WindowPlacement
{
    public int X { get; set; }
    public int Y { get; set; }
    public double Width { get; set; } = 1280;
    public double Height { get; set; } = 920;
    public bool Maximized { get; set; }
}

internal sealed class Settings
{
    public string ServerPath { get; set; } = FindServer();
    public string ModelRoot { get; set; } = @"D:\AI\models";
    public string SelectedModel { get; set; } = "";
    public bool CloseToTray { get; set; }
    public WindowPlacement? Window { get; set; }
    public Dictionary<string, string> ClientPaths { get; set; } = new();
    public Dictionary<string, Dictionary<string, LaunchProfile>> Presets { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, LaunchProfile> Profiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public static string DataRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LlamaLauncherWinUI");
    public static string FilePath => Path.Combine(DataRoot, "settings.json");
    public static Settings Load(out string warning)
    {
        warning = "";
        if (!File.Exists(FilePath)) return new();
        try
        {
            var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? throw new JsonException("设置为空。");
            settings.Profiles = new(settings.Profiles, StringComparer.OrdinalIgnoreCase);
            settings.Presets = settings.Presets.ToDictionary(p => p.Key, p => new Dictionary<string, LaunchProfile>(p.Value, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
            return settings;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        { warning = "无法读取上次设置，已使用默认值：" + ex.Message; return new(); }
    }
    public void Save()
    {
        Directory.CreateDirectory(DataRoot);
        File.WriteAllText(FilePath + ".tmp", JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(FilePath + ".tmp", FilePath, true);
    }
    private static string FindServer()
    {
        foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(folder)) continue;
            var candidate = Path.Combine(folder.Trim('"'), "llama-server.exe");
            if (File.Exists(candidate)) return candidate;
        }
        var packages = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WinGet", "Packages");
        if (Directory.Exists(packages))
            foreach (var folder in Directory.EnumerateDirectories(packages, "ggml.llamacpp_*"))
                if (File.Exists(Path.Combine(folder, "llama-server.exe"))) return Path.Combine(folder, "llama-server.exe");
        return "";
    }
}

internal sealed record ModelFile(string Path, long Bytes)
{
    public GgufInfo? Info { get; private set; }
    public string InfoError { get; private set; } = "";
    private static ModelFile Inspect(string path)
    {
        var model = new ModelFile(path, new FileInfo(path).Length);
        try { model.Info = GgufInfo.Read(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or OverflowException)
        { model.InfoError = ex.Message; }
        return model;
    }
    public string Name => System.IO.Path.GetFileNameWithoutExtension(Path);
    public string IconName
    {
        get
        {
            string architecture = Info?.Architecture ?? "";
            if (architecture.StartsWith("qwen", StringComparison.OrdinalIgnoreCase) || Name.Contains("Qwen", StringComparison.OrdinalIgnoreCase)) return "qwen-color.svg";
            if (architecture == "gpt-oss" || Name.Contains("gpt-oss", StringComparison.OrdinalIgnoreCase)) return "openai.svg";
            if (architecture.StartsWith("hunyuan", StringComparison.OrdinalIgnoreCase) || Name.Contains("HY-MT", StringComparison.OrdinalIgnoreCase)) return "hunyuan-color.svg";
            return "gguf.svg";
        }
    }
    public string Size => $"{Bytes / 1073741824d:0.00} GiB";
    public static List<ModelFile> Scan(string root)
    {
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("模型目录不存在：" + root);
        return Directory.EnumerateFiles(root, "*.gguf", SearchOption.AllDirectories)
            .Where(p => !System.IO.Path.GetFileName(p).Contains("mmproj", StringComparison.OrdinalIgnoreCase))
            .Where(p => !Regex.IsMatch(p, @"-0*(?:[2-9]|[1-9]\d+)-of-\d+\.gguf$", RegexOptions.IgnoreCase))
            .Select(Inspect)
            .OrderBy(m => m.Name.Contains("Qwen", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }
    public string[] Projectors => Directory.GetFiles(System.IO.Path.GetDirectoryName(Path)!, "*.gguf")
        .Where(p => System.IO.Path.GetFileName(p).Contains("mmproj", StringComparison.OrdinalIgnoreCase)).Order().ToArray();
}
