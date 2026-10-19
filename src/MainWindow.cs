using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;
using Windows.UI;
using Microsoft.UI.Text;

namespace LlamaLauncher;

internal sealed partial class MainWindow : Window
{
    private readonly Settings settings;
    private readonly Server server = new();
    private readonly Grid root = new();
    private readonly ListView models = new() { SelectionMode = ListViewSelectionMode.Single };
    private readonly TextBlock count = Text("扫描中", 12, true);
    private readonly TextBlock title = Text("选择一个本地模型", 27);
    private readonly ContentControl selectedIcon = new() { Content = AppImages.Model("gguf.svg", 52), Width = 52, Height = 52 };
    private readonly TextBlock modelPath = Text("", 12, true);
    private readonly TextBlock status = Text("尚未启动", 14);
    private readonly TextBlock runningModel = Text("启动后可在浏览器中聊天，或连接兼容的 API 客户端。", 12, true);
    private readonly TextBlock memory = Text("", 12, true);
    private readonly ProgressRing progress = new() { Width = 20, Height = 20, IsActive = false };
    private readonly InfoBar notice = new() { IsOpen = false, IsClosable = true };
    private readonly TextBox search = new() { PlaceholderText = "搜索本地模型", Margin = new(16, 4, 16, 10) };
    private readonly Button start = ActionButton("启动模型", Symbol.Play);
    private readonly Button stop = ActionButton("停止", Symbol.Stop);
    private readonly Button chat = ActionButton("打开聊天", Symbol.Comment);
    private readonly Button restart = ActionButton("重启", Symbol.Refresh);
    private readonly Button copyApi = ActionButton("复制 API 地址", Symbol.Copy);
    private readonly TextBox command = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, FontFamily = new("Cascadia Mono"), FontSize = 12, MinHeight = 180 };
    private readonly RichEditBox log = new() { IsReadOnly = true, TextWrapping = TextWrapping.NoWrap, FontFamily = new("Cascadia Mono"), FontSize = 12, MinHeight = 120, VerticalAlignment = VerticalAlignment.Stretch };
    private readonly StringBuilder logBuffer = new(), pendingLog = new();
    private bool resetLog;
    private string reportedFailure = "";
    private readonly CheckBox followLog = new() { Content = "跟随新日志", IsChecked = true };
    private readonly NumberBox port = Number("端口", 1, 65535, 1);
    private readonly NumberBox context = Number("上下文长度", 512, 1048576, 1024);
    private readonly ComboBox gpu = Choice("GPU 层数", ["auto", "all", "0", "16", "24", "32"], true);
    private readonly NumberBox threads = Number("CPU 线程数（0 为自动）", 0, 1024, 1);
    private readonly NumberBox batch = Number("批大小", 1, 8192, 256);
    private readonly ComboBox flash = Choice("Flash Attention", ["auto", "on", "off"]);
    private readonly ComboBox cacheK = Choice("KV 缓存 K", ["f16", "bf16", "q8_0", "q4_0", "q4_1", "q5_0", "q5_1", "iq4_nl"]);
    private readonly ComboBox cacheV = Choice("KV 缓存 V", ["f16", "bf16", "q8_0", "q4_0", "q4_1", "q5_0", "q5_1", "iq4_nl"]);
    private readonly NumberBox temp = Number("温度", 0, 2, 0.1);
    private readonly NumberBox topP = Number("Top P", 0, 1, 0.05);
    private readonly NumberBox topK = Number("Top K", 0, 1000, 1);
    private readonly NumberBox repeat = Number("重复惩罚", 0, 3, 0.05);
    private readonly ComboBox projector = new() { Header = "视觉投影文件（mmproj）", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly CheckBox autoBrowser = new() { Content = "模型就绪后自动打开聊天页面" };
    private readonly TextBox exePath = new() { Header = "llama-server.exe", TextWrapping = TextWrapping.NoWrap };
    private readonly TextBox modelRoot = new() { Header = "模型根目录（包含子文件夹）" };
    private readonly TextBlock backend = Text("", 12, true);
    private readonly DispatcherQueueTimer timer;
    private readonly Pivot tabs = new();
    private List<ModelFile> allModels = [];
    private ModelFile? selected;
    private bool loading, busy, closing, allowClose, browserOpened;

    public MainWindow()
    {
        settings = Settings.Load(out string warning);
        Title = "Llama Launcher · WinUI 3";
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico"));
        SystemBackdrop = new MicaBackdrop();
        RestorePlacement();
        ExtendsContentIntoTitleBar = true;
        root.RequestedTheme = ElementTheme.Dark;
        root.RowDefinitions.Add(new() { Height = new(46) });
        root.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        var titleBar = new Grid { Padding = new(20, 0, 140, 0) };
        var titleRow = Row(AppImages.Image("app.svg", 26), Text("Llama Launcher", 13));
        titleRow.VerticalAlignment = VerticalAlignment.Center;
        titleBar.Children.Add(titleRow); root.Children.Add(titleBar); SetTitleBar(titleBar);
        workspace.Pane = BuildSidebar(); workspace.Content = BuildMain();
        Grid.SetRow(workspace, 1); root.Children.Add(workspace);
        Content = root;
        showModels.Click += (_, _) => workspace.IsPaneOpen = !workspace.IsPaneOpen;
        root.SizeChanged += (_, _) => AdaptLayout();
        AppWindow.Changed += (_, _) => RememberPlacement();
        tabs.SelectionChanged += (_, e) =>
        {
            if (!ReferenceEquals(e.OriginalSource, tabs)) return;
            FlushLog(); UpdateClientPage(true); UpdateParameterStatus();
            if (tabs.SelectedIndex == 5) RefreshBackups();
        };
        if (settings.Window?.Maximized == true && AppWindow.Presenter is OverlappedPresenter presenter) presenter.Maximize();
        exePath.Text = settings.ServerPath; modelRoot.Text = settings.ModelRoot;
        start.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
        SetId(start, "StartServer"); SetId(stop, "StopServer"); SetId(chat, "OpenChat");
        SetId(models, "ModelList"); SetId(port, "Port"); SetId(context, "Context");
        SetId(command, "CommandPreview"); SetId(status, "ServerStatus");
        start.Click += async (_, _) => await LaunchAsync();
        restart.Click += async (_, _) => await LaunchAsync();
        stop.Click += async (_, _) => await RunAction(async () => { await CancelTestAsync(); await server.StopAsync(); });
        chat.Click += async (_, _) => await OpenUri(server.BaseUrl + "/");
        copyApi.Click += (_, _) => Copy(server.BaseUrl + "/v1");
        models.SelectionChanged += (_, _) => SelectModel();
        search.TextChanged += (_, _) => FilterModels();
        foreach (var n in new[] { port, context, threads, batch, temp, topP, topK, repeat }) n.ValueChanged += (_, _) => UpdatePreview();
        foreach (var c in new[] { gpu, flash, cacheK, cacheV, projector, thinking, effort }) c.SelectionChanged += (_, _) => UpdatePreview();
        gpu.LostFocus += (_, _) => UpdatePreview();
        autoBrowser.Click += (_, _) => UpdatePreview();
        timer = DispatcherQueue.CreateTimer(); timer.Interval = TimeSpan.FromMilliseconds(500);
        timer.Tick += (_, _) => Tick(); timer.Start();
        InitializeTray();
        AppWindow.Closing += async (_, e) =>
        {
            if (allowClose) return;
            e.Cancel = true;
            if (closing) return;
            if (settings.CloseToTray && tray is not null && !exitRequested) { HideToTray(); return; }
            if (busy) { exitRequested = true; diagnosticCancellation?.Cancel(); testCancellation?.Cancel(); return; }
            closing = true; SetBusy(true);
            try
            {
                RememberPlacement(); SaveCurrent(); settings.Save();
            }
            catch (Exception ex) { server.Log("保存设置失败：" + ex.Message); }
            try
            {
                timer.Stop(); await CancelTestAsync(); await gpuRead;
                await server.DisposeAsync(); gpuMonitor?.Dispose(); tray?.Dispose();
                allowClose = true; Close();
            }
            catch (Exception ex) { closing = false; exitRequested = false; timer.Start(); RestoreWindow(); ShowError(ex.Message); SetBusy(false); }
        };
        root.Loaded += async (_, _) =>
        {
            await RefreshModels();
            if (warning.Length > 0) ShowError(warning);
            await DetectBackend();
        };
    }

    private FrameworkElement BuildSidebar()
    {
        var panel = new Grid { Background = new SolidColorBrush(Color.FromArgb(80, 0, 0, 0)), BorderBrush = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)), BorderThickness = new(0, 0, 1, 0) };
        panel.RowDefinitions.Add(new() { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new() { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        panel.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var header = new StackPanel { Margin = new(20, 24, 20, 12), Spacing = 8 };
        header.Children.Add(Text("本地模型", 21)); header.Children.Add(count);
        panel.Children.Add(header);
        Grid.SetRow(search, 1); panel.Children.Add(search);
        models.Margin = new(8, 0, 8, 12); Grid.SetRow(models, 2); panel.Children.Add(models);
        var bottom = new StackPanel { Margin = new(16, 8, 16, 20), Spacing = 10 };
        var refresh = ActionButton("刷新模型", Symbol.Refresh);
        refresh.HorizontalAlignment = HorizontalAlignment.Stretch;
        refresh.Click += async (_, _) => await RefreshModels();
        var paths = ActionButton("路径与环境", Symbol.Setting);
        paths.HorizontalAlignment = HorizontalAlignment.Stretch;
        paths.Click += (_, _) => tabs.SelectedIndex = 3;
        bottom.Children.Add(refresh); bottom.Children.Add(paths);
        var hide = ActionButton("缩到托盘", Symbol.BackToWindow);
        hide.HorizontalAlignment = HorizontalAlignment.Stretch; hide.Click += (_, _) => HideToTray(); bottom.Children.Add(hide);
        bottom.Children.Add(Text("本机运行 · 127.0.0.1", 12, true));
        Grid.SetRow(bottom, 3); panel.Children.Add(bottom);
        return panel;
    }
    private FrameworkElement BuildMain()
    {
        var main = new Grid { Margin = new(28, 20, 28, 24), RowSpacing = 18 };
        main.RowDefinitions.Add(new() { Height = GridLength.Auto });
        main.RowDefinitions.Add(new() { Height = GridLength.Auto });
        main.RowDefinitions.Add(new() { Height = GridLength.Auto });
        main.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        main.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var heading = new StackPanel { Spacing = 6 };
        title.MaxLines = 2; title.TextTrimming = TextTrimming.CharacterEllipsis;
        heading.Children.Add(Row(showModels, Text("MODEL WORKSPACE", 11, true)));
        var modelHeading = new Grid { ColumnSpacing = 14 };
        modelHeading.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        modelHeading.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        modelHeading.Children.Add(selectedIcon); title.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(title, 1); modelHeading.Children.Add(title); heading.Children.Add(modelHeading);
        modelPath.TextWrapping = TextWrapping.Wrap;
        modelPath.MaxLines = 2; modelPath.TextTrimming = TextTrimming.CharacterEllipsis;
        modelPath.IsTextSelectionEnabled = true; heading.Children.Add(modelPath);
        main.Children.Add(heading);
        var state = new Grid { ColumnSpacing = 12 };
        state.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        state.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        state.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        state.Children.Add(progress);
        var stateText = new StackPanel { Spacing = 6 }; stateText.Children.Add(status); stateText.Children.Add(runningModel);
        Grid.SetColumn(stateText, 1); state.Children.Add(stateText); Grid.SetColumn(memory, 2); state.Children.Add(memory);
        var stateGroup = new StackPanel { Spacing = 10 };
        resourceExpander.Content = BuildResourceCard();
        stateGroup.Children.Add(Card(state)); stateGroup.Children.Add(resourceExpander);
        Grid.SetRow(stateGroup, 1); main.Children.Add(stateGroup);
        var noticeHost = new ScrollViewer { Content = notice, MaxHeight = 140, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(noticeHost, 2); main.Children.Add(noticeHost);
        tabs.Items.Add(new PivotItem { Header = "启动参数", Content = new ScrollViewer { Content = BuildParameters(), VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
        tabs.Items.Add(new PivotItem { Header = "运行日志", Content = BuildLogs() });
        var preview = new StackPanel { Spacing = 12, Margin = new(0, 12, 0, 0) };
        preview.Children.Add(Text("将直接传递给 llama-server 的启动参数", 13, true)); preview.Children.Add(command);
        var copy = ActionButton("复制命令", Symbol.Copy); copy.Click += (_, _) => Copy(command.Text); preview.Children.Add(copy);
        tabs.Items.Add(new PivotItem { Header = "命令预览", Content = new ScrollViewer { Content = preview } });
        tabs.Items.Add(new PivotItem { Header = "路径设置", Content = new ScrollViewer { Content = BuildPaths() } });
        tabs.Items.Add(new PivotItem { Header = "模型测试", Content = BuildTestPane() });
        tabs.Items.Add(new PivotItem { Header = "客户端接入", Content = BuildClientsPane() });
        Grid.SetRow(tabs, 3); main.Children.Add(tabs);
        var actions = Fields(start, stop, restart, chat);
        Grid.SetRow(actions, 4); main.Children.Add(actions);
        return main;
    }
    private StackPanel BuildParameters()
    {
        var parameters = new StackPanel { Spacing = 14, Margin = new(0, 12, 8, 8) };
        parameters.Children.Add(Card(modelInfo)); parameters.Children.Add(BuildPresetCard());
        parameters.Children.Add(BuildParameterStatus());
        var basic = new StackPanel { Spacing = 14 };
        basic.Children.Add(Text("基础设置", 17));
        basic.Children.Add(Text("每个模型单独记住参数；GPU 层数 auto 自动分配，all 尽量全部放入显存。", 12, true));
        basic.Children.Add(Fields(port, context, gpu));
        basic.Children.Add(projector); basic.Children.Add(autoBrowser);
        basic.Children.Add(Fields(thinking, effort)); basic.Children.Add(capability);
        parameters.Children.Add(Card(basic));
        var advanced = new StackPanel { Spacing = 16 };
        advanced.Children.Add(Fields(threads, batch, flash));
        advanced.Children.Add(Fields(cacheK, cacheV));
        advanced.Children.Add(Text("采样参数", 15));
        advanced.Children.Add(Fields(temp, topP)); advanced.Children.Add(Fields(topK, repeat));
        var expander = new Expander { Header = "高级参数", Content = advanced, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        parameters.Children.Add(expander);
        var save = ActionButton("保存此模型的参数", Symbol.Save);
        save.Click += (_, _) =>
        {
            try { SaveCurrent(); settings.Save(); MarkParametersSaved(); ShowNotice("已保存", "该模型下次会使用这组参数。", InfoBarSeverity.Success); }
            catch (Exception ex) { ShowError(ex.Message); }
        };
        parameters.Children.Add(save);
        parameters.Children.Add(Text("修改参数后，点击启动或重启使其生效。托盘运行选项在路径设置中。", 12, true));
        return parameters;
    }
    private Grid BuildLogs()
    {
        var pane = new Grid { RowSpacing = 10, Margin = new(0, 12, 0, 0) };
        pane.RowDefinitions.Add(new() { Height = GridLength.Auto });
        pane.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        pane.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var clear = ActionButton("清空显示", Symbol.Clear); clear.Click += (_, _) => { logBuffer.Clear(); pendingLog.Clear(); resetLog = false; log.Document.SetText(TextSetOptions.None, ""); };
        var logs = ActionButton("打开日志文件夹", Symbol.Folder); logs.Click += (_, _) => OpenFolder(Path.Combine(Settings.DataRoot, "logs"));
        pane.Children.Add(Fields(clear, logs, followLog)); Grid.SetRow(log, 1); pane.Children.Add(log);
        return pane;
    }
    private StackPanel BuildPaths()
    {
        var pane = new StackPanel { Spacing = 18, Margin = new(0, 12, 8, 8) };
        pane.Children.Add(Text("使用已有的 llama.cpp 和本地 GGUF 文件", 17));
        pane.Children.Add(exePath);
        var chooseExe = ActionButton("选择 llama-server.exe", Symbol.OpenFile);
        chooseExe.Click += async (_, _) =>
        {
            var picker = new FileOpenPicker(); picker.FileTypeFilter.Add(".exe");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            var file = await picker.PickSingleFileAsync(); if (file is not null) exePath.Text = file.Path;
        };
        pane.Children.Add(chooseExe); pane.Children.Add(modelRoot);
        var chooseRoot = ActionButton("选择模型文件夹", Symbol.Folder);
        chooseRoot.Click += async (_, _) =>
        {
            var picker = new FolderPicker(); picker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            var folder = await picker.PickSingleFolderAsync(); if (folder is not null) modelRoot.Text = folder.Path;
        };
        pane.Children.Add(chooseRoot);
        var apply = ActionButton("保存路径并重新扫描", Symbol.Accept);
        apply.Click += async (_, _) =>
        {
            try
            {
                if (!File.Exists(exePath.Text.Trim())) throw new FileNotFoundException("llama-server.exe 路径无效。");
                if (!Directory.Exists(modelRoot.Text.Trim())) throw new DirectoryNotFoundException("模型目录不存在。");
                SaveCurrent(); settings.ServerPath = exePath.Text.Trim(); settings.ModelRoot = modelRoot.Text.Trim(); settings.Save();
                await RefreshModels(); await DetectBackend(); UpdatePreview();
                ShowNotice("路径已保存", "启动时将使用这里指定的程序和模型目录。", InfoBarSeverity.Success);
            }
            catch (Exception ex) { ShowError(ex.Message); }
        };
        pane.Children.Add(apply); pane.Children.Add(Card(backend));
        pane.Children.Add(BuildTraySettings());
        pane.Children.Add(Text("程序设置与日志保存在：\n" + Settings.DataRoot, 12, true));
        pane.Children.Add(Text("界面与工作流程参考 lop008/llama-gui-launcher，以 C# / WinUI 3 独立实现。", 12, true));
        var reference = new HyperlinkButton { Content = "查看参考项目", NavigateUri = new("https://github.com/lop008/llama-gui-launcher") };
        pane.Children.Add(reference); return pane;
    }

    private async Task RefreshModels()
    {
        if (busy || closing) return;
        SetBusy(true);
        try
        {
            SaveCurrent();
            allModels = await Task.Run(() => ModelFile.Scan(settings.ModelRoot));
            if (selected is not null && !allModels.Any(m => m.Path.Equals(selected.Path, StringComparison.OrdinalIgnoreCase)))
            {
                selected = null; title.Text = "选择一个本地模型"; modelPath.Text = ""; command.Text = "";
                UpdateSelectedIcon("gguf.svg");
                modelInfo.Text = ""; capability.Text = ""; presets.Items.Clear(); presetName.Text = "";
            }
            FilterModels();
            if (allModels.Count == 0) ShowNotice("没有找到模型", "请在路径设置中选择包含 GGUF 文件的目录。", InfoBarSeverity.Warning);
        }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { SetBusy(false); }
    }
    private void FilterModels()
    {
        if (loading) return;
        try { if (selected is not null) Server.ValidateProfile(Capture()); } catch (Exception ex) { ShowError(ex.Message); return; }
        string selectedPath = selected?.Path ?? settings.SelectedModel;
        loading = true; models.Items.Clear();
        var filtered = allModels.Where(m => m.Name.Contains(search.Text, StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var model in filtered)
        {
            var item = new ListViewItem { Tag = model, Padding = new(12, 14, 12, 14), HorizontalContentAlignment = HorizontalAlignment.Stretch };
            var layout = new StackPanel { Spacing = 6 };
            var name = Text(model.Name, 14); name.TextWrapping = TextWrapping.Wrap;
            layout.Children.Add(name); layout.Children.Add(Text(model.Size + "   ·   GGUF", 12, true));
            var entry = new Grid { ColumnSpacing = 12 };
            entry.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            entry.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            entry.Children.Add(AppImages.Model(model.IconName, 38)); Grid.SetColumn(layout, 1); entry.Children.Add(layout);
            item.Content = entry;
            ToolTipService.SetToolTip(item, model.Path); models.Items.Add(item);
            if (model.Path.Equals(selectedPath, StringComparison.OrdinalIgnoreCase)) models.SelectedItem = item;
        }
        count.Text = $"{allModels.Count} 个模型";
        loading = false;
        if (models.SelectedItem is null && filtered.Count > 0 && selected is null) models.SelectedIndex = 0;
        else if (models.SelectedItem is not null) SelectModel();
    }
    private void SelectModel()
    {
        if (loading || models.SelectedItem is not ListViewItem { Tag: ModelFile model }) return;
        if (ReferenceEquals(selected, model)) return;
        try { SaveCurrent(); }
        catch (Exception ex)
        {
            ShowError(ex.Message); loading = true;
            models.SelectedItem = models.Items.OfType<ListViewItem>().FirstOrDefault(i => (i.Tag as ModelFile)?.Path == selected?.Path);
            loading = false; return;
        }
        selected = model; settings.SelectedModel = model.Path;
        UpdateSelectedIcon(model.IconName);
        title.Text = model.Name; modelPath.Text = model.Size + "  ·  " + model.Path;
        var p = settings.Profiles.GetValueOrDefault(model.Path) ?? new LaunchProfile();
        savedParameterSnapshot = p.Clone();
        LoadProfile(p); RefreshPresets();
        if (workspace.DisplayMode == SplitViewDisplayMode.Overlay) workspace.IsPaneOpen = false;
        try { settings.Save(); } catch (Exception ex) { ShowError(ex.Message); }
    }
    private void LoadProfile(LaunchProfile p)
    {
        if (selected is not { } model) return;
        loading = true;
        port.Value = p.Port; context.Value = p.Context; gpu.Text = p.GpuLayers;
        threads.Value = p.Threads; batch.Value = p.Batch; flash.SelectedItem = p.FlashAttention;
        cacheK.SelectedItem = p.CacheK; cacheV.SelectedItem = p.CacheV;
        temp.Value = p.Temperature; topP.Value = p.TopP; topK.Value = p.TopK; repeat.Value = p.RepeatPenalty;
        autoBrowser.IsChecked = p.OpenBrowser;
        projector.Items.Clear(); projector.Items.Add(new ComboBoxItem { Content = "不使用（纯文本）", Tag = "" });
        foreach (var path in model.Projectors) projector.Items.Add(new ComboBoxItem { Content = Path.GetFileName(path), Tag = path });
        projector.SelectedIndex = 0;
        for (int i = 1; i < projector.Items.Count; i++) if (((ComboBoxItem)projector.Items[i]).Tag?.ToString() == p.Projector) projector.SelectedIndex = i;
        projector.IsEnabled = projector.Items.Count > 1;
        LoadModelFeatures(p);
        loading = false; UpdatePreview(); UpdateState();
    }
    private void UpdateSelectedIcon(string file) => selectedIcon.Content = AppImages.Model(file, 52);
    private LaunchProfile Capture() => new()
    {
        Port = Integer(port), Context = Integer(context), GpuLayers = gpu.Text.Trim(), Threads = Integer(threads), Batch = Integer(batch),
        FlashAttention = flash.SelectedItem?.ToString() ?? "auto", CacheK = cacheK.SelectedItem?.ToString() ?? "f16", CacheV = cacheV.SelectedItem?.ToString() ?? "f16",
        Temperature = temp.Value, TopP = topP.Value, TopK = Integer(topK), RepeatPenalty = repeat.Value,
        Projector = (projector.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "", OpenBrowser = autoBrowser.IsChecked == true,
        ThinkingMode = thinking.SelectedItem?.ToString() ?? "auto", ReasoningEffort = effort.SelectedItem?.ToString() ?? "default"
    };
    private static int Integer(NumberBox number)
    {
        if (!double.IsFinite(number.Value) || number.Value != Math.Truncate(number.Value)) throw new ArgumentException(number.Header + "必须填写整数。");
        return checked((int)number.Value);
    }
    private void SaveCurrent()
    {
        if (selected is null || loading) return;
        var profile = Capture(); Server.ValidateProfile(profile);
        settings.Profiles[selected.Path] = profile;
    }
    private void UpdatePreview()
    {
        if (loading || selected is null) return;
        try { command.Text = Server.Command(settings.ServerPath, Server.Arguments(settings.ServerPath, Capture(), selected.Path, selected.Info)); }
        catch (Exception ex) { command.Text = ex.Message; }
        UpdateParameterStatus();
        UpdateClientPage();
    }
    private async Task LaunchAsync()
    {
        if (selected is null) return;
        await RunAction(async () =>
        {
            var p = Capture(); Server.Arguments(settings.ServerPath, p, selected.Path);
            SaveCurrent(); settings.Save(); MarkParametersSaved(); notice.IsOpen = false;
            await CancelTestAsync(); await server.StopAsync(); browserOpened = false; reportedFailure = "";
            await server.StartAsync(settings.ServerPath, p, selected.Path);
        });
    }
    private async Task RunAction(Func<Task> action, Action<Exception>? failed = null)
    {
        if (busy || closing) return;
        SetBusy(true);
        try { await action(); }
        catch (Exception ex) { server.Log("操作失败：" + ex); ShowError(Errors.Explain(ex)); failed?.Invoke(ex); }
        finally { SetBusy(false); }
    }
    private void SetBusy(bool value)
    {
        busy = value; models.IsEnabled = !value; search.IsEnabled = !value; clientPicker.IsEnabled = !value; UpdateState();
        if (!value && exitRequested && !closing) DispatcherQueue.TryEnqueue(() => Close());
    }
    private void Tick()
    {
        for (int i = 0; i < 200 && server.Messages.TryDequeue(out string? message); i++) { logBuffer.AppendLine(message); pendingLog.AppendLine(message); }
        if (logBuffer.Length > 120000) { logBuffer.Remove(0, logBuffer.Length - 100000); resetLog = true; pendingLog.Clear(); }
        FlushLog();
        UpdateState();
        gpuRetry.IsEnabled = !closing && gpuRead.IsCompleted;
        if (!closing && gpuFailures < 3 && DateTime.UtcNow >= nextGpuRead && gpuRead.IsCompleted) gpuRead = ReadGpuAsync();
        if (!server.Running && server.State.StartsWith("服务已退出", StringComparison.Ordinal) && reportedFailure != server.State)
        { reportedFailure = server.State; ShowError(server.State + "\n" + (server.FailureDetails.Length > 0 ? Errors.Explain(new IOException(server.FailureDetails)) : "请点击查看日志，检查模型和启动参数。")); }
        if (server.Ready && !browserOpened)
        {
            browserOpened = true;
            if (settings.Profiles.GetValueOrDefault(server.ModelPath)?.OpenBrowser == true) _ = OpenUri(server.BaseUrl + "/");
        }
    }
    private void UpdateState()
    {
        bool stateChanged = status.Text != server.State; SetText(status, server.State);
        if (stateChanged) status.Foreground = server.Ready ? new SolidColorBrush(Color.FromArgb(255, 108, 219, 173)) : new SolidColorBrush(Colors.White);
        progress.IsActive = server.Running && !server.Ready;
        progress.Visibility = progress.IsActive ? Visibility.Visible : Visibility.Collapsed;
        SetText(runningModel, server.Running ? Path.GetFileNameWithoutExtension(server.ModelPath) + "  ·  " + server.BaseUrl : "启动后可在浏览器中聊天，或连接兼容的 API 客户端。");
        if (AppWindow.IsVisible)
        {
            try { SetText(memory, server.Running ? $"进程内存 {server.MemoryGiB:0.00} GiB" : ""); }
            catch (InvalidOperationException) { SetText(memory, ""); }
        }
        start.IsEnabled = !busy && selected is not null && (!server.Running || server.ModelPath != selected.Path);
        SetText((TextBlock)((StackPanel)start.Content).Children[1], server.Running && server.ModelPath != selected?.Path ? "切换到此模型" : "启动模型");
        stop.IsEnabled = !busy && server.Running; restart.IsEnabled = !busy && server.Running && server.ModelPath == selected?.Path;
        chat.IsEnabled = server.Ready; copyApi.IsEnabled = server.Ready;
        UpdateFeatureState();
        UpdateClientPage();
        if (tabs.SelectedIndex == 0 && AppWindow.IsVisible) UpdateParameterStatus();
    }
    private async Task DetectBackend()
    {
        if (!File.Exists(settings.ServerPath)) { backend.Text = "尚未找到 llama-server，请选择程序路径。"; return; }
        try
        {
            using var child = new Process { StartInfo = new(settings.ServerPath, "--list-devices") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = Path.GetDirectoryName(settings.ServerPath)! } };
            child.Start(); var output = child.StandardOutput.ReadToEndAsync(); var errors = child.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await child.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { child.Kill(true); await child.WaitForExitAsync(); throw new IOException("设备检测超时，可查看 llama-server 安装情况。"); }
            backend.Text = (await output + "\n" + await errors).Trim();
        }
        catch (Exception ex) { backend.Text = "设备检测失败：" + ex.Message; }
    }
    private void ShowError(string message) => ShowNotice("操作未完成", message, InfoBarSeverity.Error);
    private void ShowNotice(string heading, string message, InfoBarSeverity severity)
    {
        notice.Title = heading; notice.Message = message; notice.Severity = severity; notice.IsOpen = true;
        var logs = new HyperlinkButton { Content = "查看日志" }; logs.Click += (_, _) => tabs.SelectedIndex = 1;
        notice.ActionButton = severity == InfoBarSeverity.Error ? logs : null;
    }
    private void Copy(string text, string? message = null)
    {
        try { var data = new DataPackage(); data.SetText(text); Clipboard.SetContent(data); ShowNotice("已复制", message ?? text, InfoBarSeverity.Success); }
        catch (Exception ex) { ShowError("复制失败：" + ex.Message); }
    }
    private async Task OpenUri(string uri)
    {
        try { if (!await Windows.System.Launcher.LaunchUriAsync(new Uri(uri))) ShowError("无法打开浏览器：" + uri); }
        catch (Exception ex) { ShowError(ex.Message); }
    }
    private void OpenFolder(string path)
    {
        try { Directory.CreateDirectory(path); Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) { ShowError(ex.Message); }
    }
    private static TextBlock Text(string value, double size, bool muted = false) => new()
    { Text = value, FontSize = size, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(muted ? Color.FromArgb(255, 165, 175, 190) : Colors.White) };
    private static Border Card(UIElement content) => new()
    { Child = content, CornerRadius = new(10), Padding = new(18), Background = new SolidColorBrush(Color.FromArgb(22, 255, 255, 255)), BorderBrush = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)), BorderThickness = new(1) };
    private static StackPanel Row(params UIElement[] children)
    { var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 }; foreach (var child in children) row.Children.Add(child); return row; }
    private static Button ActionButton(string text, Symbol icon) => new()
    { Content = Row(new SymbolIcon(icon) { Width = 16, Height = 16 }, Text(text, 14)), Padding = new(14, 10, 14, 10) };
    private static NumberBox Number(string header, double min, double max, double step) => new()
    { Header = header, Minimum = min, Maximum = max, SmallChange = step, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact, HorizontalAlignment = HorizontalAlignment.Stretch };
    private static ComboBox Choice(string header, string[] items, bool editable = false)
    { var box = new ComboBox { Header = header, IsEditable = editable, HorizontalAlignment = HorizontalAlignment.Stretch }; foreach (var item in items) box.Items.Add(item); return box; }
    private static Grid Fields(params FrameworkElement[] fields)
    {
        var grid = new Grid { ColumnSpacing = 16, RowSpacing = 12 };
        foreach (var field in fields) grid.Children.Add(field);
        int previous = -1;
        void Reflow(double width)
        {
            bool actions = fields.All(f => f is Button or CheckBox);
            int columns = width < 540 ? (actions ? Math.Min(2, fields.Length) : 1) : width < 780 ? Math.Min(2, fields.Length) : fields.Length;
            if (columns == previous || columns == 0) return; previous = columns;
            grid.ColumnDefinitions.Clear(); grid.RowDefinitions.Clear();
            for (int i = 0; i < columns; i++) grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            for (int i = 0; i < (fields.Length + columns - 1) / columns; i++) grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
            for (int i = 0; i < fields.Length; i++) { Grid.SetRow(fields[i], i / columns); Grid.SetColumn(fields[i], i % columns); }
        }
        grid.SizeChanged += (_, e) => Reflow(e.NewSize.Width); Reflow(1000);
        return grid;
    }
    private void FlushLog()
    {
        if (tabs.SelectedIndex != 1 || !AppWindow.IsVisible || (pendingLog.Length == 0 && !resetLog)) return;
        var selection = log.Document.Selection; int start = selection.StartPosition, end = selection.EndPosition;
        if (resetLog) log.Document.SetText(TextSetOptions.None, logBuffer.ToString());
        else { selection.EndKey(TextRangeUnit.Story, false); selection.Text = pendingLog.ToString(); }
        if (followLog.IsChecked == true) { selection.EndKey(TextRangeUnit.Story, false); selection.ScrollIntoView(PointOptions.Start); }
        else selection.SetRange(start, end);
        pendingLog.Clear(); resetLog = false;
    }
    private static void SetText(TextBlock block, string value) { if (block.Text != value) block.Text = value; }
    private static void SetId(DependencyObject element, string id) => AutomationProperties.SetAutomationId(element, id);
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint window);
}
