using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace LlamaLauncher;

internal sealed partial class MainWindow
{
    private readonly SplitView workspace = new() { OpenPaneLength = 308, IsPaneOpen = true, DisplayMode = SplitViewDisplayMode.Inline,
        PaneBackground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 16, 24, 39)) };
    private readonly Button showModels = ActionButton("模型列表", Symbol.GlobalNavigationButton);
    private readonly Expander resourceExpander = new() { Header = "显存与连接信息", IsExpanded = true,
        HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private bool? compactMode, resourceExpandedMode;

    private void RestorePlacement()
    {
        if (settings.Window is { } saved) AppWindow.Move(new(saved.X, saved.Y));
        double scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96d;
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        int width = Math.Min((int)Math.Round((settings.Window?.Width ?? 1280) * scale), area.Width);
        int height = Math.Min((int)Math.Round((settings.Window?.Height ?? 920) * scale), area.Height);
        width = Math.Max(Math.Min((int)(560 * scale), area.Width), width);
        height = Math.Max(Math.Min((int)(480 * scale), area.Height), height);
        int x = settings.Window?.X ?? area.X + (area.Width - width) / 2;
        int y = settings.Window?.Y ?? area.Y + (area.Height - height) / 2;
        AppWindow.MoveAndResize(new RectInt32(Math.Clamp(x, area.X, area.X + area.Width - width),
            Math.Clamp(y, area.Y, area.Y + area.Height - height), width, height));
    }
    private void RememberPlacement()
    {
        if (!AppWindow.IsVisible || AppWindow.Presenter is not OverlappedPresenter presenter || presenter.State == OverlappedPresenterState.Minimized) return;
        settings.Window ??= new();
        settings.Window.Maximized = presenter.State == OverlappedPresenterState.Maximized;
        if (settings.Window.Maximized) return;
        double scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96d;
        settings.Window.X = AppWindow.Position.X; settings.Window.Y = AppWindow.Position.Y;
        settings.Window.Width = AppWindow.Size.Width / scale; settings.Window.Height = AppWindow.Size.Height / scale;
    }
    private void AdaptLayout()
    {
        if (root.ActualWidth <= 0) return;
        bool compact = root.ActualWidth < 1120;
        if (compact != compactMode)
        {
            compactMode = compact;
            workspace.DisplayMode = compact ? SplitViewDisplayMode.Overlay : SplitViewDisplayMode.Inline;
            workspace.IsPaneOpen = !compact;
            showModels.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
        }
        workspace.OpenPaneLength = Math.Clamp(root.ActualWidth - 48, 0, 308);
        bool expand = root.ActualHeight >= 820 && root.ActualWidth >= 1120;
        if (expand != resourceExpandedMode) { resourceExpandedMode = expand; resourceExpander.IsExpanded = expand; }
        title.FontSize = root.ActualWidth < 800 ? 22 : 27;
    }
}
