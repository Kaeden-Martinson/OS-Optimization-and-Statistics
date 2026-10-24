using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace PCSpecOverlay.Core;

/// <summary>Gives a window the dark Windows title bar so it matches the app's dark theme.</summary>
public static class DarkTitleBar
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public static void Apply(Window window)
    {
        var dark = 1; // DWMWA_USE_IMMERSIVE_DARK_MODE
        DwmSetWindowAttribute(new WindowInteropHelper(window).Handle, 20, ref dark, sizeof(int));
    }
}
