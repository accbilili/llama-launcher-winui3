using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Runtime.InteropServices;
using Microsoft.Windows.AppLifecycle;

namespace LlamaLauncher;

internal static class Program
{
    private static LauncherApp? app;
    [STAThread]
    private static void Main()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Record("Unhandled: " + e.ExceptionObject);
        try { Run(); }
        catch (Exception ex) { Record(ex.ToString()); MessageBox(0, "客户端无法启动：" + ex.Message, "Llama Launcher", 0x10); }
    }
    internal static void Record(string text)
    {
        Directory.CreateDirectory(Settings.DataRoot);
        File.AppendAllText(Path.Combine(Settings.DataRoot, "client-errors.log"), $"{DateTime.Now:O} {text}\n");
    }
    private static void Run()
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        var instance = AppInstance.FindOrRegisterForKey("LlamaLauncher.WinUI3");
        if (!instance.IsCurrent)
        {
            AllowSetForegroundWindow(instance.ProcessId);
            using var completed = new EventWaitHandle(false, EventResetMode.ManualReset);
            Exception? failure = null;
            var args = AppInstance.GetCurrent().GetActivatedEventArgs();
            _ = Task.Run(async () =>
            {
                try { await instance.RedirectActivationToAsync(args); }
                catch (Exception ex) { failure = ex; }
                finally { completed.Set(); }
            });
            Marshal.ThrowExceptionForHR(CoWaitForMultipleHandles(0, uint.MaxValue, 1, [completed.SafeWaitHandle.DangerousGetHandle()], out _));
            if (failure is not null) throw failure;
            return;
        }
        instance.Activated += (_, _) => app?.ShowMainWindow();
        Application.Start(p =>
        {
            SynchronizationContext.SetSynchronizationContext(new Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()));
            app = new LauncherApp();
        });
        instance.UnregisterKey();
    }
    [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(uint processId);
    [DllImport("ole32.dll")] private static extern int CoWaitForMultipleHandles(uint flags, uint milliseconds, uint count, nint[] handles, out uint index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(nint hwnd, string text, string caption, uint type);
}

public sealed partial class LauncherApp : Application
{
    private MainWindow? window;
    public LauncherApp()
    {
        RequestedTheme = ApplicationTheme.Dark;
        UnhandledException += (_, e) =>
        {
            Directory.CreateDirectory(Settings.DataRoot);
            File.AppendAllText(Path.Combine(Settings.DataRoot, "client-errors.log"), $"{DateTime.Now:O} {e.Exception}\n");
        };
        InitializeComponent();
    }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        window = new MainWindow();
        window.Activate();
    }
    internal void ShowMainWindow() => window?.DispatcherQueue.TryEnqueue(() => window.RestoreWindow());
}
