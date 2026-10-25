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

    public static AppTheme Resolve(string appearance, Func<object?> readAppsUseLightTheme) => appearance switch
    {
        Settings.Light => AppTheme.Light,
        Settings.Dark => AppTheme.Dark,
        _ => WindowsTheme(readAppsUseLightTheme),
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
