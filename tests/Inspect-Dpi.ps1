param([string]$ReportPath)
$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class LlamaDpiAudit {
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")] public static extern IntPtr GetWindowDpiAwarenessContext(IntPtr window);
    [DllImport("user32.dll")] public static extern bool AreDpiAwarenessContextsEqual(IntPtr first, IntPtr second);
    [DllImport("shcore.dll")] public static extern int GetProcessDpiAwareness(IntPtr process, out int awareness);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("shcore.dll")] public static extern int GetScaleFactorForMonitor(IntPtr monitor, out int scale);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr window, out RECT rectangle);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr window, uint attribute, out RECT rectangle, int size);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left,Top,Right,Bottom; }
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr LoadLibraryEx(string file, IntPtr reserved, uint flags);
    [DllImport("kernel32.dll")] public static extern IntPtr FindResource(IntPtr module, IntPtr name, IntPtr type);
    [DllImport("kernel32.dll")] public static extern IntPtr LoadResource(IntPtr module, IntPtr resource);
    [DllImport("kernel32.dll")] public static extern IntPtr LockResource(IntPtr resource);
    [DllImport("kernel32.dll")] public static extern uint SizeofResource(IntPtr module, IntPtr resource);
    [DllImport("kernel32.dll")] public static extern bool FreeLibrary(IntPtr module);
    public static string Manifest(string path) {
        var module=LoadLibraryEx(path,IntPtr.Zero,2);
        try {
            var resource=FindResource(module,(IntPtr)1,(IntPtr)24);
            var bytes=new byte[SizeofResource(module,resource)];
            Marshal.Copy(LockResource(LoadResource(module,resource)),bytes,0,bytes.Length);
            return System.Text.Encoding.UTF8.GetString(bytes);
        } finally { FreeLibrary(module); }
    }
}
'@
$client = Get-Process -Name LlamaLauncher -ErrorAction Stop
if (@($client).Count -ne 1) { throw '预期只有一个客户端进程。' }
$awareness = 0
[LlamaDpiAudit]::GetProcessDpiAwareness($client.Handle, [ref]$awareness) | Out-Null
$windowContext = [LlamaDpiAudit]::GetWindowDpiAwarenessContext($client.MainWindowHandle)
$monitorScale = 0
[LlamaDpiAudit]::GetScaleFactorForMonitor([LlamaDpiAudit]::MonitorFromWindow($client.MainWindowHandle,2),[ref]$monitorScale) | Out-Null
$rectangle = [LlamaDpiAudit+RECT]::new()
[LlamaDpiAudit]::GetWindowRect($client.MainWindowHandle,[ref]$rectangle) | Out-Null
$physicalRectangle = [LlamaDpiAudit+RECT]::new()
[LlamaDpiAudit]::DwmGetWindowAttribute($client.MainWindowHandle,9,[ref]$physicalRectangle,16) | Out-Null
$report = [pscustomobject]@{
    ProcessId = $client.Id
    ProcessDpiAwareness = $awareness
    WindowDpi = [LlamaDpiAudit]::GetDpiForWindow($client.MainWindowHandle)
    IsPerMonitorV2 = [LlamaDpiAudit]::AreDpiAwarenessContextsEqual($windowContext, [IntPtr](-4))
    MonitorScalePercent = $monitorScale
    CallerVirtualizedWidth = $rectangle.Right - $rectangle.Left
    CallerVirtualizedHeight = $rectangle.Bottom - $rectangle.Top
    VisiblePhysicalWidth = $physicalRectangle.Right - $physicalRectangle.Left
    VisiblePhysicalHeight = $physicalRectangle.Bottom - $physicalRectangle.Top
    ManifestDpiAwareness = ([regex]::Match([LlamaDpiAudit]::Manifest($client.Path), '<dpiAwareness[^>]*>([^<]+)</dpiAwareness>')).Groups[1].Value
}
$json = $report | ConvertTo-Json -Depth 3
if ($ReportPath) { $json | Set-Content -LiteralPath $ReportPath -Encoding utf8 }
$json
