using System.ComponentModel;
using System.Runtime.InteropServices;

namespace LlamaLauncher;

internal sealed class TrayIcon : IDisposable
{
    private const uint Callback = 0x8001;
    private readonly nint window;
    private readonly SubclassProc procedure;
    private readonly Action<int> action;
    private readonly Func<bool> ready, running;
    private readonly Action<string> error;
    private readonly uint taskbarCreated = RegisterWindowMessageW("TaskbarCreated");
    private readonly nint icon;
    private NotifyData data;
    public TrayIcon(nint window, Action<int> action, Func<bool> ready, Func<bool> running, Action<string> error)
    {
        this.window = window; this.action = action; this.ready = ready; this.running = running; this.error = error;
        procedure = WindowProc;
        int size = GetSystemMetricsForDpi(49, GetDpiForWindow(window));
        icon = LoadImageW(0, Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico"), 1, size, size, 0x10);
        if (icon == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "无法加载应用托盘图标。");
        data = new() { Size = (uint)Marshal.SizeOf<NotifyData>(), Window = window, Id = 1, Flags = 7, Message = Callback,
            Icon = icon, Tip = "Llama Launcher", Info = "", InfoTitle = "" };
        if (!SetWindowSubclass(window, procedure, 1, 0)) { int code = Marshal.GetLastWin32Error(); DestroyIcon(icon); throw new Win32Exception(code); }
        if (!Add()) { RemoveWindowSubclass(window, procedure, 1); DestroyIcon(icon); throw new IOException("Windows 未能创建托盘图标。"); }
    }
    private bool Add()
    {
        if (!Shell_NotifyIconW(0, ref data)) return false;
        data.Version = 4; Shell_NotifyIconW(4, ref data); return true;
    }
    public void Update(string text)
    {
        text = text.Length > 127 ? text[..127] : text;
        if (data.Tip == text) return;
        data.Tip = text; Shell_NotifyIconW(1, ref data);
    }
    private nint WindowProc(nint hwnd, uint message, nuint wParam, nint lParam, nuint id, nuint reference)
    {
        try
        {
            if (message == taskbarCreated && !Add()) error("资源管理器重启后无法恢复托盘图标，请保持主窗口打开。");
            if (message == Callback)
            {
                uint evt = unchecked((uint)(long)lParam) & 0xFFFF;
                if (evt is 0x400 or 0x401) action(1);
                else if (evt == 0x7B) Menu();
                return 0;
            }
        }
        catch (Exception ex) { error("托盘操作失败：" + ex.Message); }
        return DefSubclassProc(hwnd, message, wParam, lParam);
    }
    private void Menu()
    {
        nint menu = CreatePopupMenu();
        if (menu == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            AppendMenuW(menu, 0, 1, "显示主窗口");
            AppendMenuW(menu, ready() ? 0u : 1u, 2, "打开聊天");
            AppendMenuW(menu, running() ? 0u : 1u, 3, "停止模型");
            AppendMenuW(menu, 0x800, 0, ""); AppendMenuW(menu, 0, 4, "退出并停止服务");
            GetCursorPos(out var point); SetForegroundWindow(window);
            int command = TrackPopupMenuEx(menu, 0x102, point.X, point.Y, window, 0);
            PostMessageW(window, 0, 0, 0);
            if (command > 0) action(command);
        }
        finally { DestroyMenu(menu); }
    }
    public void Dispose() { Shell_NotifyIconW(2, ref data); RemoveWindowSubclass(window, procedure, 1); DestroyIcon(icon); }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyData
    {
        public uint Size; public nint Window; public uint Id, Flags, Message; public nint Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags; public Guid Guid; public nint Balloon;
    }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate nint SubclassProc(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint data);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool Shell_NotifyIconW(uint message, ref NotifyData data);
    [DllImport("comctl32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowSubclass(nint window, SubclassProc proc, nuint id, nuint data);
    [DllImport("comctl32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool RemoveWindowSubclass(nint window, SubclassProc proc, nuint id);
    [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint window, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint LoadImageW(nint instance, string name, uint type, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(nint icon);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint window);
    [DllImport("user32.dll")] private static extern int GetSystemMetricsForDpi(int index, uint dpi);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessageW(string message);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool AppendMenuW(nint menu, uint flags, nuint id, string text);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")] private static extern int TrackPopupMenuEx(nint menu, uint flags, int x, int y, nint window, nint parameters);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(nint menu);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool PostMessageW(nint window, uint message, nuint wParam, nint lParam);
}
