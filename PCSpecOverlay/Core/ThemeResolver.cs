using Microsoft.Win32;

namespace PCSpecOverlay.Core;

public enum AppTheme { Dark, Light }

/// <summary>
/// Turns the Appearance setting into a theme. Match Windows follows the Windows "app mode" setting
/// (AppsUseLightTheme = 1 means light); anything missing or unreadable falls back to dark.
/// </summary>
public static class ThemeResolver
{
    public const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    /// <summary>Whether the Windows taskbar is light (SystemUsesLightTheme = 1); anything else counts as dark.</summary>
    public static bool TaskbarIsLight(Func<object?> readSystemUsesLightTheme)
    {
        try { return readSystemUsesLightTheme() is int v && v == 1; }
        catch { return false; }
    }

    public static object? ReadSystemUsesLightTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
        return key?.GetValue("SystemUsesLightTheme");
    }

    public static AppTheme Resolve(string appearance, Func<object?> readAppsUseLightTheme) => appearance switch
    {
        Settings.Light => AppTheme.Light,
        Settings.Dark => AppTheme.Dark,
        _ => WindowsTheme(readAppsUseLightTheme),
    };

    /// <summary>The overlay's theme: its own choice, or the app's when set to "Same as app".</summary>
    public static AppTheme ResolveOverlay(string overlayTheme, AppTheme appTheme) => overlayTheme switch
    {
        Settings.AlwaysLight => AppTheme.Light,
        Settings.SameAsApp => appTheme,
        _ => AppTheme.Dark,
    };

    private static AppTheme WindowsTheme(Func<object?> read)
    {
        try { return read() is int v && v == 1 ? AppTheme.Light : AppTheme.Dark; }
        catch (Exception e)
        {
            Log.Warn("Theme", $"Couldn't read the Windows app theme, using dark: {e.Message}");
            return AppTheme.Dark;
        }
    }

    public static object? ReadAppsUseLightTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
        return key?.GetValue("AppsUseLightTheme");
    }
}
