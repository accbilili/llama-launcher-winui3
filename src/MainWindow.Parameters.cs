using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LlamaLauncher;

internal sealed partial class MainWindow
{
    private readonly TextBlock parameterState = Text("选择模型后设置参数。", 13, true);
    private readonly Button restoreParameters = ActionButton("恢复当前运行参数", Symbol.Undo);
    private LaunchProfile? savedParameterSnapshot;
    private void MarkParametersSaved()
    {
        if (selected is not null) savedParameterSnapshot = settings.Profiles.GetValueOrDefault(selected.Path)?.Clone() ?? new();
        UpdateParameterStatus();
    }
    private FrameworkElement BuildParameterStatus()
    {
        var pane = new StackPanel { Spacing = 10 };
        pane.Children.Add(parameterState); pane.Children.Add(restoreParameters);
        restoreParameters.Click += (_, _) =>
        {
            if (!server.Running || selected?.Path != server.ModelPath || server.ActiveProfile is not { } active) return;
            LoadProfile(active.Clone());
            ShowNotice("已恢复运行参数", "界面已恢复为当前服务使用的参数；不会重启模型。点击保存可记住这组参数。", InfoBarSeverity.Success);
        };
        return Card(pane);
    }
    private void UpdateParameterStatus()
    {
        bool active = server.Running && selected?.Path == server.ModelPath && server.ActiveProfile is not null;
        restoreParameters.IsEnabled = active && !busy;
        if (loading || selected is null) { SetText(parameterState, "选择模型后设置参数。"); return; }
        try
        {
            var current = Capture(); Server.ValidateProfile(current);
            var saved = savedParameterSnapshot ?? new();
            bool unsaved = !current.SameServerParameters(saved) || current.OpenBrowser != saved.OpenBrowser;
            string saveState = unsaved ? "尚未保存" : "已保存";
            if (active)
            {
                bool changed = !current.SameServerParameters(server.ActiveProfile!);
                SetText(parameterState, changed ? $"{saveState} · 已修改，尚未重启生效。当前模型继续使用原参数。" : $"{saveState} · 参数与当前运行配置一致。");
                restoreParameters.IsEnabled = changed && !busy;
            }
            else SetText(parameterState, $"{saveState} · 此模型尚未运行，参数在启动时生效。");
        }
        catch (Exception ex) { SetText(parameterState, "参数无效：" + Errors.Explain(ex) + " 请修正后再保存或切换模型。"); }
    }
}
