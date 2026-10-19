using System.Runtime.InteropServices;

namespace LlamaLauncher;

// DXGI 标识显卡并读取专用显存容量；PDH 读取整卡专用显存占用。
internal sealed class GpuMonitor : IDisposable
{
    private nint query;
    private readonly List<(string Name, ulong Total, nint Counter)> adapters = [];
    public GpuMonitor()
    {
        nint factory = 0;
        try
        {
            Marshal.ThrowExceptionForHR(CreateDXGIFactory1(new Guid("770aae78-f26f-4dba-a829-253c83d1b387"), out factory));
            Check(PdhOpenQueryW(null, 0, out query));
            var enumerate = Method<EnumAdapters>(factory, 12);
            for (uint i = 0; ; i++)
            {
                int result = enumerate(factory, i, out nint adapter);
                if (result == unchecked((int)0x887A0002)) break;
                Marshal.ThrowExceptionForHR(result);
                try
                {
                    Marshal.ThrowExceptionForHR(Method<GetDescription>(adapter, 10)(adapter, out var desc));
                    if ((desc.Flags & 2) != 0 || desc.DedicatedVideoMemory == 0) continue;
                    string path = $@"\GPU Adapter Memory(luid_0x{unchecked((uint)desc.High):x8}_0x{desc.Low:x8}_phys_*)\Dedicated Usage";
                    uint status = PdhAddEnglishCounterW(query, path, 0, out nint counter);
                    adapters.Add((desc.DescriptionText, (ulong)desc.DedicatedVideoMemory, status == 0 ? counter : 0));
                }
                finally { Marshal.Release(adapter); }
            }
        }
        catch { Dispose(); throw; }
        finally { if (factory != 0) Marshal.Release(factory); }
    }
    public string Read()
    {
        Check(PdhCollectQueryData(query));
        if (adapters.Count == 0) return "未找到具有专用显存的显卡。";
        return string.Join("\n", adapters.Select(a =>
        {
            long? used = Usage(a.Counter);
            return used is { } bytes
                ? $"{a.Name}  ·  已用 {bytes / 1073741824d:0.00} / {a.Total / 1073741824d:0.00} GiB  ·  剩余 {Math.Max(0, a.Total / 1073741824d - bytes / 1073741824d):0.00} GiB"
                : $"{a.Name}  ·  总量 {a.Total / 1073741824d:0.00} GiB  ·  当前占用不可用";
        }));
    }
    private static long? Usage(nint counter)
    {
        if (counter == 0) return null;
        uint bytes = 0, count = 0;
        uint status = PdhGetFormattedCounterArrayW(counter, 0x400, ref bytes, ref count, 0);
        if (status != 0x800007D2 || bytes == 0) return null;
        nint buffer = Marshal.AllocHGlobal(checked((int)bytes));
        try
        {
            if (PdhGetFormattedCounterArrayW(counter, 0x400, ref bytes, ref count, buffer) != 0) return null;
            long sum = 0; bool valid = false;
            for (int i = 0; i < count; i++)
            {
                var item = Marshal.PtrToStructure<CounterItem>(buffer + i * Marshal.SizeOf<CounterItem>());
                if (item.Value.Status <= 1 && item.Value.Large >= 0) { sum += item.Value.Large; valid = true; }
            }
            return valid ? sum : null;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }
    private static T Method<T>(nint instance, int slot) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * nint.Size));
    private static void Check(uint result) { if (result != 0) throw new IOException($"Windows 显存计数器不可用（0x{result:X8}）。"); }
    public void Dispose() { if (query != 0) { PdhCloseQuery(query); query = 0; } }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Description
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DescriptionText;
        public uint Vendor, Device, Subsystem, Revision;
        public nuint DedicatedVideoMemory, DedicatedSystemMemory, SharedSystemMemory;
        public uint Low; public int High; public uint Flags;
    }
    [StructLayout(LayoutKind.Explicit, Size = 16)]
    private struct CounterValue { [FieldOffset(0)] public uint Status; [FieldOffset(8)] public long Large; }
    [StructLayout(LayoutKind.Sequential)]
    private struct CounterItem { public nint Name; public CounterValue Value; }
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int EnumAdapters(nint factory, uint index, out nint adapter);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetDescription(nint adapter, out Description description);
    [DllImport("dxgi.dll", ExactSpelling = true)] private static extern int CreateDXGIFactory1(in Guid iid, out nint factory);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhOpenQueryW(string? source, nuint data, out nint query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhAddEnglishCounterW(nint query, string path, nuint data, out nint counter);
    [DllImport("pdh.dll")] private static extern uint PdhCollectQueryData(nint query);
    [DllImport("pdh.dll")] private static extern uint PdhCloseQuery(nint query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhGetFormattedCounterArrayW(nint counter, uint format, ref uint bytes, ref uint count, nint buffer);
}
