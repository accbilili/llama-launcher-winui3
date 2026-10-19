using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Runtime.InteropServices;

namespace LlamaLauncher;

internal sealed partial class MainWindow
{
    private readonly TextBlock modelInfo = Text("", 12, true);
    private readonly TextBlock gpuInfo = Text("正在读取显卡信息…", 12, true);
    private readonly TextBlock connection = Text("启动模型后显示 API 地址和模型 ID。", 12, true);
    private readonly Button copyModel = ActionButton("复制模型 ID", Symbol.Copy);
    private readonly ComboBox thinking = Choice("思考模式", ["auto", "on", "off"]);
    private readonly ComboBox effort = Choice("推理强度", ["default", "low", "medium", "high"]);
    private readonly TextBlock capability = Text("", 12, true);
    private readonly ComboBox presets = new() { Header = "已保存的预设", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBox presetName = new() { Header = "预设名称", MaxLength = 64, PlaceholderText = "输入名称后保存" };
    private readonly CheckBox closeToTray = new() { Content = "关闭主窗口时缩到托盘，保持模型运行" };
    private readonly ComboBox testMode = Choice("测试方式", ["文本生成", "翻译（HY-MT2 等）"]);
    private readonly ComboBox language = Choice("翻译目标语言", ["简体中文", "繁體中文", "English", "日本語", "한국어", "Français", "Deutsch"]);
    private readonly NumberBox testLimit = Number("输出上限（含思考 tokens）", 16, 16384, 256);
    private readonly TextBox testPrompt = new() { Header = "测试输入 / 待翻译文本", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 90, MaxHeight = 180,
        Text = "请用两句话介绍你自己。" };
    private readonly TextBox testOutput = new() { Header = "模型回复", IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 120, MaxHeight = 360 };
    private readonly TextBox testReasoning = new() { Header = "思考内容（服务端返回时显示）", IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 70, MaxHeight = 220 };
    private readonly TextBlock testStats = Text("首个生成片段包含思考内容；速度使用服务端统计。", 12, true);
    private readonly TextBlock testTarget = Text("请先启动一个模型。", 12, true);
    private readonly Button testStart = ActionButton("发送测试", Symbol.Play);
    private readonly Button testCancel = ActionButton("取消测试", Symbol.Cancel);
    private TrayIcon? tray;
    private GpuMonitor? gpuMonitor;
    private Task gpuRead = Task.CompletedTask, testTask = Task.CompletedTask;
    private CancellationTokenSource? testCancellation;
    private bool exitRequested;
    private int gpuFailures;
    private DateTime nextGpuRead = DateTime.MinValue;
    private readonly Button gpuRetry = ActionButton("重新检测显存", Symbol.Refresh);

    private FrameworkElement BuildResourceCard()
    {
        var pane = new StackPanel { Spacing = 8 };
        pane.Children.Add(gpuInfo);
        gpuRetry.Click += (_, _) =>
        {
            if (!gpuRead.IsCompleted || closing) return;
            gpuMonitor?.Dispose(); gpuMonitor = null; gpuFailures = 0; gpuRead = ReadGpuAsync();
        };
        pane.Children.Add(gpuRetry);
        pane.Children.Add(Text("专用显存为整张显卡的系统占用，每 2 秒刷新；剩余量不等同于模型可用预算。", 11, true));
        pane.Children.Add(connection); connection.IsTextSelectionEnabled = true;
        pane.Children.Add(Fields(copyApi, copyModel));
        copyModel.Click += (_, _) => Copy(server.ModelId);
        return Card(pane);
    }
    private FrameworkElement BuildPresetCard()
    {
        var pane = new StackPanel { Spacing = 12 };
        pane.Children.Add(Text("参数预设", 17)); pane.Children.Add(Fields(presets, presetName));
        presets.SelectionChanged += (_, _) => { if (presets.SelectedItem is string name) presetName.Text = name; };
        var load = ActionButton("加载预设", Symbol.OpenFile);
        var save = ActionButton("保存 / 覆盖", Symbol.Save);
        var delete = ActionButton("删除预设", Symbol.Delete);
        load.Click += (_, _) =>
        {
            if (selected is null || presets.SelectedItem is not string name) return;
            try
            {
                LoadProfile(settings.Presets[selected.Path][name].Clone());
                SaveCurrent(); settings.Save(); MarkParametersSaved(); ShowNotice("预设已加载", "启动或重启后生效。", InfoBarSeverity.Success);
            }
            catch (Exception ex) { ShowError(ex.Message); }
        };
        save.Click += (_, _) =>
        {
            if (selected is null) return;
            try
            {
                string name = presetName.Text.Trim();
                if (name.Length == 0) throw new ArgumentException("请填写预设名称。");
                var p = Capture(); Server.ValidateProfile(p);
                settings.Presets[selected.Path][name] = p.Clone(); SaveCurrent(); settings.Save(); MarkParametersSaved(); RefreshPresets(name);
                ShowNotice("预设已保存", name, InfoBarSeverity.Success);
            }
            catch (Exception ex) { ShowError(ex.Message); }
        };
        delete.Click += (_, _) =>
        {
            if (selected is null || presets.SelectedItem is not string name) return;
            try { settings.Presets[selected.Path].Remove(name); settings.Save(); RefreshPresets(); presetName.Text = ""; }
            catch (Exception ex) { ShowError(ex.Message); }
        };
        pane.Children.Add(Fields(load, save, delete));
        pane.Children.Add(Text("加载后可继续修改；修改不会自动覆盖命名预设。初始三组仅作起点，不代表显存适配结果。", 12, true));
        return Card(pane);
    }
    private void RefreshPresets(string? name = null)
    {
        presets.Items.Clear();
        if (selected is null) return;
        if (!settings.Presets.TryGetValue(selected.Path, out var entries))
        {
            var basis = (settings.Profiles.GetValueOrDefault(selected.Path) ?? new()).Clone();
            int Limit(int n) => selected.Info?.Context is > 0 ? (int)Math.Clamp(Math.Min((ulong)n, selected.Info.Context.Value), 512UL, 1048576UL) : n;
            var daily = basis.Clone(); daily.Context = Limit(8192);
            var longer = basis.Clone(); longer.Context = Limit(16384);
            var low = basis.Clone(); low.Context = Limit(4096); low.GpuLayers = "0";
            settings.Presets[selected.Path] = entries = new(StringComparer.OrdinalIgnoreCase)
                { ["日常聊天"] = daily, ["长文本"] = longer, ["低显存（CPU）"] = low };
        }
        foreach (string key in entries.Keys) presets.Items.Add(key);
        if (name is not null) presets.SelectedItem = name;
    }
    private void LoadModelFeatures(LaunchProfile p)
    {
        var info = selected?.Info;
        modelInfo.Text = info is null ? "元数据读取失败：" + selected?.InfoError
            : $"架构：{info.Architecture}  ·  文件声明量化：{info.Quantization}  ·  训练上下文：{(info.Context.HasValue ? info.Context.Value.ToString("N0") : "未声明")}\n模型名称：{info.Name}  ·  {selected!.Size}（所选文件）";
        thinking.IsEnabled = info?.Thinking == true; effort.IsEnabled = info?.Reasoning == true;
        thinking.SelectedItem = thinking.IsEnabled ? p.ThinkingMode : "auto";
        effort.SelectedItem = effort.IsEnabled ? p.ReasoningEffort : "default";
        capability.Text = (thinking.IsEnabled ? "模板包含 enable_thinking，可设置思考开关。" : "模板未声明思考开关，使用模型默认行为。")
            + (effort.IsEnabled ? " 模板包含 reasoning_effort，可调整推理强度。" : "") + " 修改后需重启。";
    }
    private FrameworkElement BuildTestPane()
    {
        var pane = new StackPanel { Spacing = 14, Margin = new(0, 12, 8, 8) };
        pane.Children.Add(testTarget);
        pane.Children.Add(Text("测试发送给当前正在运行的模型；不随左侧待启动模型切换。", 12, true));
        testMode.SelectedIndex = 0; language.SelectedIndex = 0; language.IsEnabled = false; testLimit.Value = 1024;
        testMode.SelectionChanged += (_, _) => language.IsEnabled = testMode.SelectedIndex == 1;
        pane.Children.Add(Fields(testMode, language, testLimit)); pane.Children.Add(testPrompt);
        pane.Children.Add(Fields(testStart, testCancel)); pane.Children.Add(testStats); pane.Children.Add(testOutput); pane.Children.Add(testReasoning);
        var copy = ActionButton("复制回复", Symbol.Copy); copy.Click += (_, _) => Copy(testOutput.Text); pane.Children.Add(copy);
        testStart.Click += async (_, _) => { testTask = SendTestAsync(); await testTask; };
        testCancel.Click += (_, _) => testCancellation?.Cancel();
        return new ScrollViewer { Content = pane, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }
    private async Task SendTestAsync()
    {
        if (!server.Ready || testCancellation is not null || busy) return;
        try
        {
            string prompt = testPrompt.Text.Trim();
            if (prompt.Length == 0) throw new ArgumentException("请输入测试文本。");
            int limit = Integer(testLimit);
            if (limit is < 16 or > 16384) throw new ArgumentException("输出上限必须在 16–16384 之间。");
            if (testMode.SelectedIndex == 1) prompt = $"Translate the following text into {language.SelectedItem}. Output only the translation.\n\n{prompt}";
            testCancellation = new(); testOutput.Text = ""; testReasoning.Text = ""; testStats.Text = "正在发送…"; UpdateFeatureState();
            await ModelTest.RunAsync(server.BaseUrl, server.ModelId, prompt, limit, new Progress<TestUpdate>(update =>
            { testOutput.Text = update.Text; testReasoning.Text = update.Reasoning; testStats.Text = update.Statistics; }), testCancellation.Token);
        }
        catch (Exception ex) { testStats.Text += "\n" + Errors.Explain(ex); server.Log("模型测试失败：" + ex); ShowError("模型测试失败：" + Errors.Explain(ex)); }
        finally { testCancellation?.Dispose(); testCancellation = null; UpdateFeatureState(); }
    }
    private async Task CancelTestAsync()
    { testCancellation?.Cancel(); await testTask; }

    private FrameworkElement BuildTraySettings()
    {
        var pane = new StackPanel { Spacing = 10 };
        closeToTray.IsChecked = settings.CloseToTray;
        closeToTray.Click += (_, _) =>
        {
            try { settings.CloseToTray = closeToTray.IsChecked == true; settings.Save(); }
            catch (Exception ex) { ShowError(ex.Message); }
        };
        pane.Children.Add(closeToTray); pane.Children.Add(Text("默认关闭即退出并停止本客户端启动的服务。托盘菜单可随时退出。", 12, true));
        return Card(pane);
    }
    private void InitializeTray()
    {
        try
        {
            tray = new(WinRT.Interop.WindowNative.GetWindowHandle(this), command => DispatcherQueue.TryEnqueue(async () =>
            {
                if (command == 1) RestoreWindow();
                else if (command == 2 && server.Ready) await OpenUri(server.BaseUrl + "/");
                else if (command == 3) await RunAction(async () => { await CancelTestAsync(); await server.StopAsync(); });
                else if (command == 4) { exitRequested = true; Close(); }
            }), () => server.Ready, () => server.Running, message => DispatcherQueue.TryEnqueue(() => { RestoreWindow(); ShowError(message); }));
        }
        catch (Exception ex) { ShowError("托盘不可用，关闭窗口将直接退出：" + ex.Message); }
    }
    internal void RestoreWindow()
    {
        if (closing) return;
        AppWindow.Show();
        if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter { State: Microsoft.UI.Windowing.OverlappedPresenterState.Minimized } presenter) presenter.Restore();
        Activate(); SetForegroundWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
    }
    private void HideToTray()
    {
        if (tray is null) { ShowError("托盘图标不可用，无法隐藏窗口。"); return; }
        try { RememberPlacement(); SaveCurrent(); settings.Save(); MarkParametersSaved(); AppWindow.Hide(); }
        catch (Exception ex) { ShowError(ex.Message); }
    }
    private async Task ReadGpuAsync()
    {
        gpuRetry.IsEnabled = false;
        try
        {
            string value = await Task.Run(() => { gpuMonitor ??= new(); return gpuMonitor.Read(); });
            SetText(gpuInfo, value); gpuFailures = 0; nextGpuRead = DateTime.UtcNow.AddSeconds(2);
        }
        catch (Exception ex)
        {
            gpuMonitor?.Dispose(); gpuMonitor = null; gpuFailures++; nextGpuRead = DateTime.UtcNow.AddSeconds(5);
            SetText(gpuInfo, $"显存读取失败（{gpuFailures}/3）：{ex.Message}\n" + (gpuFailures < 3 ? "5 秒后自动重试，也可以点击重新检测。" : "已暂停自动重试，请点击重新检测。"));
        }
        finally { gpuRetry.IsEnabled = !closing; }
    }
    private void UpdateFeatureState()
    {
        copyModel.IsEnabled = server.Ready;
        SetText(connection, server.Running ? $"API Base URL：{server.BaseUrl}/v1\n模型 ID：{server.ModelId}" : "启动模型后显示 API 地址和模型 ID。");
        if (tabs.SelectedIndex == 4) SetText(testTarget, server.Running ? $"测试目标：{server.ModelId}  ·  {server.State}" : "请先启动一个模型。");
        testStart.IsEnabled = server.Ready && !busy && testCancellation is null;
        testCancel.IsEnabled = testCancellation is not null;
        tray?.Update("Llama Launcher · " + server.State + (server.Running ? " · " + server.ModelId : ""));
    }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetForegroundWindow(nint window);
}
