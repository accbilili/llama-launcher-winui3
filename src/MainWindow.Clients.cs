using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Windows.Storage.Pickers;

namespace LlamaLauncher;

internal sealed partial class MainWindow
{
    private readonly ComboBox clientPicker = Choice("接入客户端", ["WorkBuddy", "Cherry Studio", "DeepSeek Harness"]);
    private readonly TextBlock clientTarget = Text("选择模型后生成连接配置。", 13, true);
    private readonly TextBox clientAddress = new() { Header = "客户端需要的地址", IsReadOnly = true };
    private readonly TextBox clientModel = new() { Header = "模型 ID（需完整一致）", IsReadOnly = true };
    private readonly TextBox clientInstructions = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 180, MaxHeight = 330 };
    private readonly TextBox clientConfig = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, FontFamily = new("Cascadia Mono"), FontSize = 12, MinHeight = 140, MaxHeight = 320 };
    private readonly TextBlock clientDiagnostic = Text("“检查连接”只读取健康状态和模型列表，不发送生成请求。", 12, true);
    private readonly TextBox clientExecutable = new() { Header = "客户端程序路径（可自行选择）" };
    private readonly Button clientPrimary = ActionButton("写入 WorkBuddy", Symbol.Save);
    private readonly Button clientCheck = ActionButton("检查连接", Symbol.Refresh);
    private readonly Button clientCopyAddress = ActionButton("复制地址", Symbol.Copy);
    private readonly Button clientCopyModel = ActionButton("复制模型 ID", Symbol.Copy);
    private readonly Button clientCopyConfig = ActionButton("复制配置", Symbol.Copy);
    private readonly Button clientExport = ActionButton("导出配置", Symbol.Save);
    private readonly Button basicCheck = ActionButton("基础回复测试", Symbol.Play);
    private readonly Button streamingCheck = ActionButton("流式测试", Symbol.Play);
    private readonly Button toolsCheck = ActionButton("工具调用测试", Symbol.Play);
    private readonly Button cancelDiagnostic = ActionButton("取消自检", Symbol.Cancel);
    private CancellationTokenSource? diagnosticCancellation;
    private Task diagnosticTask = Task.CompletedTask;
    private ClientConnection? lastClientConnection;
    private LocalClient lastClientKind = (LocalClient)(-1);
    private ClientConnection? clientConnection;
    private LocalClient CurrentClient => (LocalClient)Math.Max(0, clientPicker.SelectedIndex);

    private FrameworkElement BuildClientsPane()
    {
        var pane = new StackPanel { Spacing = 14, Margin = new(0, 12, 8, 8) };
        pane.Children.Add(clientPicker); clientPicker.SelectedIndex = 0;
        clientPicker.SelectionChanged += (_, _) => { RefreshClientPath(); UpdateClientPage(true); RefreshBackups(); };
        pane.Children.Add(clientTarget);
        pane.Children.Add(Text("有模型运行时使用运行中的地址和参数；没有运行时可预先配置左侧模型。切换模型后，请在客户端同步选择新的模型 ID。", 12, true));
        pane.Children.Add(clientAddress); pane.Children.Add(clientModel);
        var copyKey = ActionButton("复制占位密钥", Symbol.Copy);
        copyKey.Click += (_, _) => Copy("local", "已复制本地服务使用的占位密钥 local。");
        clientCopyAddress.Click += (_, _) => { if (clientConnection is { } c) Copy(c.Address(CurrentClient)); };
        clientCopyModel.Click += (_, _) => { if (clientConnection is { } c) Copy(c.ModelId); };
        pane.Children.Add(Fields(clientCopyAddress, clientCopyModel, copyKey));
        pane.Children.Add(clientInstructions);
        pane.Children.Add(Text("工具调用和思考标记按聊天模板判断；图片标记要求启动参数选中 mmproj。能力标记需要你在客户端实测。", 12, true));
        clientPrimary.Click += async (_, _) => await ApplyClientAsync();
        clientCheck.Click += async (_, _) => { diagnosticTask = RunClientDiagnostic(DiagnosticMode.Service); await diagnosticTask; };
        basicCheck.Click += async (_, _) => { diagnosticTask = RunClientDiagnostic(DiagnosticMode.Basic); await diagnosticTask; };
        streamingCheck.Click += async (_, _) => { diagnosticTask = RunClientDiagnostic(DiagnosticMode.Streaming); await diagnosticTask; };
        toolsCheck.Click += async (_, _) => { diagnosticTask = RunClientDiagnostic(DiagnosticMode.Tools); await diagnosticTask; };
        cancelDiagnostic.Click += (_, _) => diagnosticCancellation?.Cancel();
        pane.Children.Add(Fields(clientPrimary, clientCheck));
        pane.Children.Add(Fields(basicCheck, streamingCheck, toolsCheck, cancelDiagnostic));
        pane.Children.Add(Text("三种测试会实际请求当前模型，仅点击后执行。工具测试使用无副作用的回显函数；验证服务能力，不代替客户端中的实际聊天测试。", 12, true));
        clientDiagnostic.IsTextSelectionEnabled = true; pane.Children.Add(clientDiagnostic);
        clientCopyConfig.Click += (_, _) => { if (clientConnection is { } c) Copy(ClientConfigText(c), "已复制当前客户端的配置。"); };
        clientExport.Click += async (_, _) => await ExportClientAsync();
        pane.Children.Add(Fields(clientCopyConfig, clientExport));
        pane.Children.Add(new Expander { Header = "查看生成配置", Content = clientConfig, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch });
        pane.Children.Add(BuildBackupPane());
        pane.Children.Add(clientExecutable);
        var choose = ActionButton("选择客户端程序", Symbol.OpenFile);
        choose.Click += async (_, _) =>
        {
            try
            {
                var picker = new FileOpenPicker(); picker.FileTypeFilter.Add(".exe");
                WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
                var file = await picker.PickSingleFileAsync();
                if (file is null) return;
                clientExecutable.Text = file.Path; settings.ClientPaths[CurrentClient.ToString()] = file.Path; settings.Save();
            }
            catch (Exception ex) { ShowError(ex.Message); }
        };
        var open = ActionButton("打开客户端", Symbol.OpenFile); open.Click += (_, _) => OpenSelectedClient();
        pane.Children.Add(Fields(choose, open));
        pane.Children.Add(Text("先启动模型并保持本启动器运行；可以缩到托盘。退出启动器会停止所属服务，客户端届时无法连接。", 12, true));
        RefreshClientPath();
        SetId(clientPicker, "ClientPicker"); SetId(clientAddress, "ClientAddress"); SetId(clientModel, "ClientModelId");
        SetId(clientPrimary, "ApplyClientConfiguration"); SetId(clientCheck, "CheckClientConnection");
        return new ScrollViewer { Content = pane, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }
    private ClientConnection? CaptureClientConnection()
    {
        string path; LaunchProfile profile; string rootUrl;
        if (server.Running && server.ActiveProfile is { } active)
        { path = server.ModelPath; profile = active; rootUrl = server.BaseUrl; }
        else
        {
            if (selected is null) return null;
            path = selected.Path; profile = Capture(); Server.ValidateProfile(profile); rootUrl = $"http://127.0.0.1:{profile.Port}";
        }
        var info = allModels.FirstOrDefault(m => m.Path.Equals(path, StringComparison.OrdinalIgnoreCase))?.Info;
        return new(rootUrl, Path.GetFileNameWithoutExtension(path), profile.Context,
            info?.Template.Contains("tools", StringComparison.Ordinal) == true,
            !string.IsNullOrEmpty(profile.Projector), info?.Thinking == true || info?.Reasoning == true);
    }
    private void UpdateClientPage(bool force = false)
    {
        if (loading) return;
        if (!force && (tabs.SelectedIndex != 5 || !AppWindow.IsVisible)) return;
        try
        {
            clientConnection = CaptureClientConnection();
            SetText(clientTarget, clientConnection is null ? "选择模型后生成连接配置。"
                : (server.Running ? "当前运行模型 · " + server.State : "所选模型预配置 · 尚未启动") + "\n" + clientConnection.ModelId);
            if (clientConnection != lastClientConnection || CurrentClient != lastClientKind)
            {
                lastClientConnection = clientConnection; lastClientKind = CurrentClient;
                clientAddress.Text = clientConnection?.Address(CurrentClient) ?? "";
                clientModel.Text = clientConnection?.ModelId ?? "";
                clientInstructions.Text = clientConnection?.Instructions(CurrentClient) ?? "请先在左侧选择模型。";
                clientConfig.Text = clientConnection is { } c ? ClientConfigText(c) : "";
                clientPrimary.Content = Text(CurrentClient switch { LocalClient.WorkBuddy => "写入 WorkBuddy", LocalClient.CherryStudio => "导入 Cherry Studio", _ => "复制 Harness 填写信息" }, 14);
                clientDiagnostic.Text = "“检查连接”只读取健康状态和模型列表，不发送生成请求。";
            }
        }
        catch (Exception ex) { clientConnection = null; clientTarget.Text = "连接配置无法生成：" + ex.Message; }
        foreach (var button in new[] { clientPrimary, clientCheck, clientCopyAddress, clientCopyModel, clientCopyConfig, clientExport, basicCheck, streamingCheck, toolsCheck }) button.IsEnabled = clientConnection is not null && !busy;
        cancelDiagnostic.IsEnabled = diagnosticCancellation is not null;
        restoreBackup.IsEnabled = !busy && backupPicker.SelectedItem is ComboBoxItem;
    }
    private string ClientConfigText(ClientConnection c) => CurrentClient switch
    {
        LocalClient.WorkBuddy => c.WorkBuddyJson(),
        LocalClient.CherryStudio => c.Instructions(CurrentClient) + "\n\n导入链接：\n" + c.CherryImportUri(),
        _ => c.DshYaml()
    };
    private async Task ApplyClientAsync()
    {
        if (clientConnection is not { } c) return;
        var kind = CurrentClient;
        if (kind == LocalClient.DeepSeekHarness)
        { Copy(c.Instructions(kind), "已复制 Harness 填写信息。请在 Harness 的设置 → 模型 → 自定义模型 API 中添加。"); return; }
        await RunAction(async () =>
        {
            if (kind == LocalClient.WorkBuddy)
            {
                string backup = await Task.Run(() => c.SaveWorkBuddy(ClientConnection.WorkBuddyPath));
                RefreshBackups();
                ShowNotice("WorkBuddy 配置已写入", "请在 WorkBuddy 重新选择 " + c.ModelId + "。" + (backup.Length > 0 ? "\n原配置备份：" + backup : ""), InfoBarSeverity.Success);
            }
            else
            {
                bool opened = await Windows.System.Launcher.LaunchUriAsync(new Uri(c.CherryImportUri()));
                if (!opened) throw new IOException("未能打开 Cherry Studio 导入链接。请先安装 / 打开客户端注册协议，或按页面信息手动填写。");
                ShowNotice("已发送导入请求", "请在 Cherry Studio 确认服务商，然后获取模型列表并添加 " + c.ModelId + "。", InfoBarSeverity.Informational);
            }
        });
    }
    private async Task RunClientDiagnostic(DiagnosticMode mode)
    {
        if (busy || diagnosticCancellation is not null || clientConnection is not { } c) return;
        diagnosticCancellation = new();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(diagnosticCancellation.Token, timeout.Token);
        UpdateClientPage(true);
        try
        {
            await RunAction(async () =>
            {
                await CancelTestAsync();
                clientDiagnostic.Text = $"正在检测 {c.ModelId} · {c.Root} …";
                using var http = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = Timeout.InfiniteTimeSpan };
                try { clientDiagnostic.Text = $"检测目标：{c.ModelId} · {c.Root}\n" + await ClientDiagnostics.RunAsync(http, c, mode, linked.Token); }
                catch (OperationCanceledException) when (diagnosticCancellation.IsCancellationRequested) { clientDiagnostic.Text = "自检已取消。"; }
                catch (OperationCanceledException) when (timeout.IsCancellationRequested) { clientDiagnostic.Text = "自检已超时（3 分钟）。请检查模型状态，或降低上下文 / 思考开销后重试。"; }
            }, ex => clientDiagnostic.Text = "自检未通过：" + Errors.Explain(ex));
        }
        finally { diagnosticCancellation.Dispose(); diagnosticCancellation = null; UpdateClientPage(true); }
    }
    private async Task ExportClientAsync()
    {
        if (clientConnection is not { } c) return;
        string content = ClientConfigText(c); var kind = CurrentClient;
        try
        {
            var picker = new FileSavePicker { SuggestedFileName = kind + "-" + c.ModelId };
            picker.FileTypeChoices.Add("客户端配置", [kind switch { LocalClient.WorkBuddy => ".json", LocalClient.DeepSeekHarness => ".yml", _ => ".txt" }]);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            var file = await picker.PickSaveFileAsync();
            if (file is null) return;
            await File.WriteAllTextAsync(file.Path, content);
            ShowNotice("配置已导出", file.Path, InfoBarSeverity.Success);
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }
    private void RefreshClientPath()
    {
        string key = CurrentClient.ToString();
        string programs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs");
        string[] candidates = CurrentClient switch
        {
            LocalClient.WorkBuddy => [Path.Combine(programs, "WorkBuddy", "WorkBuddy.exe")],
            LocalClient.CherryStudio => [Path.Combine(programs, "Cherry Studio", "Cherry Studio.exe")],
            _ => [Path.Combine(programs, "DeepSeek Harness", "DeepSeek Harness.exe")]
        };
        clientExecutable.Text = settings.ClientPaths.GetValueOrDefault(key) ?? candidates.FirstOrDefault(File.Exists) ?? "";
    }
    private void OpenSelectedClient()
    {
        try
        {
            string path = clientExecutable.Text.Trim().Trim('"');
            if (!File.Exists(path)) throw new FileNotFoundException("请选择有效的客户端程序。");
            settings.ClientPaths[CurrentClient.ToString()] = path; settings.Save();
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(path)! });
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }
}
