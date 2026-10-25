using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;

namespace PCSpecOverlay.Core;

/// <summary>
/// Owns the app's color dictionary. Every view uses DynamicResource, so replacing the one merged
/// dictionary that holds the colors recolors open windows immediately. With Match Windows, a change
/// to the Windows app theme switches the app without a restart.
/// </summary>
public sealed class ThemeManager : IDisposable
{
    private readonly ResourceDictionary _target;
    private readonly Func<AppTheme, ResourceDictionary> _load;
    private readonly Func<object?> _readWindows;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly bool _listening;
    private ResourceDictionary? _colors;
    private string _appearance = Settings.MatchWindows;

    public ThemeManager(ResourceDictionary target, Func<AppTheme, ResourceDictionary>? load = null,
        Func<object?>? readWindows = null, bool listenToWindows = true)
    {
        _target = target;
        _load = load ?? LoadColors;
        _readWindows = readWindows ?? ThemeResolver.ReadAppsUseLightTheme;
        // App.xaml merges the dark colors first so the installer and early windows have them before Apply
        _colors = target.MergedDictionaries.FirstOrDefault(IsColors);
        _listening = listenToWindows;
        if (_listening) SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    public AppTheme Current { get; private set; } = AppTheme.Dark;

    public event Action<AppTheme>? Changed;

    public void Apply(string appearance)
    {
        _appearance = appearance;
        var wanted = ThemeResolver.Resolve(appearance, _readWindows);
        if (_colors != null && wanted == Current) return;

        ResourceDictionary colors;
        try { colors = _load(wanted); }
        catch (Exception e)
        {
            Log.Error("Theme", $"Couldn't load the {wanted} theme, using dark", e);
            if (wanted == AppTheme.Dark) return;
            wanted = AppTheme.Dark;
            if (_colors != null && Current == AppTheme.Dark) return;
            try { colors = _load(AppTheme.Dark); } catch { return; }
        }

        var index = _colors is null ? -1 : _target.MergedDictionaries.IndexOf(_colors);
        if (index >= 0) _target.MergedDictionaries[index] = colors;
        else _target.MergedDictionaries.Insert(0, colors);
        _colors = colors;

        var changed = Current != wanted;
        Current = wanted;
        Log.Info("Theme", $"Using the {wanted.ToString().ToLowerInvariant()} theme ({appearance})");
        if (changed) Changed?.Invoke(wanted);
    }

    internal void OnWindowsThemeChanged()
    {
        if (_appearance == Settings.MatchWindows) Apply(_appearance);
    }

    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.General) _dispatcher.BeginInvoke(OnWindowsThemeChanged);
    }

    public static ResourceDictionary LoadColors(AppTheme theme) =>
        new() { Source = new Uri($"pack://application:,,,/PCSpecOverlay;component/Themes/Colors.{theme}.xaml") };

    // App.xaml merges the dark colors by Source; checking for a token key as well means the swap can never
    // stack a new dictionary under an old one that still wins (later merged dictionaries take precedence)
    private static bool IsColors(ResourceDictionary d) =>
        d.Source?.OriginalString.Contains("Themes/Colors.") == true || d.Contains("C.Bg");

    public void Dispose()
    {
        if (_listening) SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
    }
}
