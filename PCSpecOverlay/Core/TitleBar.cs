using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace PCSpecOverlay.Core;

/// <summary>Switches a window's Windows title bar between light and dark to match the app theme.</summary>
public static class TitleBar
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private const int DwmwaUseImmersiveDarkMode = 20;

    public static void Apply(Window window, bool dark)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        var value = dark ? 1 : 0;
        DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref value, sizeof(int));
    }
}
