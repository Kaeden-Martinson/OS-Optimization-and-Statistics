using System.Runtime.InteropServices;

namespace PCSpecOverlay.Core;

internal sealed record Display(string DeviceName, Native.RECT Bounds, Native.RECT WorkArea, bool Primary, uint Dpi)
{
    public string Label => $"{(Primary ? "Main display" : DeviceName.Replace(@"\\.\", ""))} · {Bounds.Right - Bounds.Left}×{Bounds.Bottom - Bounds.Top}";
}

/// <summary>Connected monitors in physical pixels, with their DPI (works with mixed scaling).</summary>
internal static class Displays
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public Native.RECT rcMonitor, rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
    }

    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, IntPtr rect, IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFOEX info);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);

    public static List<Display> All()
    {
        var list = new List<Display>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (monitor, _, _, _) =>
        {
            var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
            if (GetMonitorInfo(monitor, ref info))
            {
                var dpi = GetDpiForMonitor(monitor, 0, out var x, out _) == 0 ? x : 96;
                list.Add(new Display(info.szDevice, info.rcMonitor, info.rcWork, (info.dwFlags & 1) != 0, dpi));
            }
            return true;
        }, IntPtr.Zero);
        return list;
    }

    /// <summary>The named monitor, falling back to the primary one if it's disconnected.</summary>
    public static Display? Find(string deviceName)
    {
        var all = All();
        return all.FirstOrDefault(d => d.DeviceName == deviceName && deviceName.Length > 0)
               ?? all.FirstOrDefault(d => d.Primary) ?? all.FirstOrDefault();
    }
}
