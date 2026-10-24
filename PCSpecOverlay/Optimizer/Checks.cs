using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace PCSpecOverlay.Optimizer;

public enum CheckState { Good, Warn, Info }

/// <summary>One row in the optimizer's system checks: a status plus an optional action button.</summary>
public abstract class Check : INotifyPropertyChanged
{
    public string Title { get; }
    public string Description { get; }
    public CheckState State { get; private set; }
    public string Status { get; private set; } = "";
    public string ActionLabel { get; private set; } = "";
    public bool HasAction => ActionLabel.Length > 0;

    protected Check(string title, string description)
    {
        Title = title;
        Description = description;
    }

    public void Refresh()
    {
        try { (State, Status) = Read(); }
        catch (Exception e) { (State, Status) = (CheckState.Info, $"Couldn't read ({e.Message})"); }
        ActionLabel = CurrentActionLabel();
        foreach (var p in new[] { nameof(State), nameof(Status), nameof(ActionLabel), nameof(HasAction) })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
    }

    public void RunAction()
    {
        RunActionCore();
        Refresh();
    }

    protected abstract (CheckState, string) Read();
    protected abstract string CurrentActionLabel();
    protected abstract void RunActionCore();

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>A setting the optimizer can change, remembering the original so it can be undone.</summary>
public abstract class Tweak : Check
{
    private readonly string _id;
    protected readonly Settings _settings;

    protected Tweak(string id, Settings settings, string title, string description) : base(title, description)
    {
        _id = id;
        _settings = settings;
    }

    public bool CanUndo => _settings.TweakBackups.ContainsKey(_id);
    public bool NeedsFix => State == CheckState.Warn;

    protected override string CurrentActionLabel() => NeedsFix ? "Fix" : CanUndo ? "Undo" : "";

    protected override void RunActionCore()
    {
        if (NeedsFix) Fix();
        else if (CanUndo) Undo();
    }

    public void Fix()
    {
        if (!CanUndo) _settings.TweakBackups[_id] = Capture();
        Apply();
        _settings.Save();
    }

    public void Undo()
    {
        if (!_settings.TweakBackups.Remove(_id, out var original)) return;
        Restore(original);
        _settings.Save();
    }

    /// <summary>Current value, serialized; null when the value doesn't exist.</summary>
    protected abstract string? Capture();
    protected abstract void Apply();
    protected abstract void Restore(string? original);
}

/// <summary>Windows power mode (Settings › System › Power › Power mode).</summary>
public static class PowerMode
{
    public static readonly Guid BestPerformance = new("ded574b5-45a0-4f42-8737-46345c09c238");
    public static readonly Guid Efficiency = new("961cc777-2547-4f9d-8174-7d86181b8a7a");
    public static readonly Guid Balanced = Guid.Empty;

    [DllImport("powrprof.dll")] private static extern uint PowerGetEffectiveOverlayScheme(out Guid scheme);
    [DllImport("powrprof.dll")] private static extern uint PowerSetActiveOverlayScheme(Guid scheme);

    public static Guid Current() => PowerGetEffectiveOverlayScheme(out var g) == 0 ? g : Balanced;

    public static void Set(Guid g)
    {
        var rc = PowerSetActiveOverlayScheme(g);
        if (rc != 0) throw new InvalidOperationException($"Windows refused the power mode change (error {rc}).");
    }

    public static string Name(Guid g) =>
        g == BestPerformance ? "Best performance" : g == Efficiency ? "Best power efficiency" : "Balanced";
}

public sealed class PowerModeTweak(Settings settings) : Tweak("power-mode", settings, "Power mode",
    "Best performance lets the CPU boost higher and sooner. Uses more battery.")
{
    protected override (CheckState, string) Read()
    {
        var g = PowerMode.Current();
        return (g == PowerMode.BestPerformance ? CheckState.Good : CheckState.Warn, PowerMode.Name(g));
    }

    protected override string? Capture() => PowerMode.Current().ToString();
    protected override void Apply() => PowerMode.Set(PowerMode.BestPerformance);
    protected override void Restore(string? original) => PowerMode.Set(Guid.TryParse(original, out var g) ? g : PowerMode.Balanced);
}

/// <summary>A per-user DWORD registry setting.</summary>
public sealed class RegistryDwordTweak(
    string id, Settings settings, string title, string description,
    string key, string name, int optimal, int whenMissing, Func<int, string> describe)
    : Tweak(id, settings, title, description)
{
    private int? Value()
    {
        using var k = Registry.CurrentUser.OpenSubKey(key);
        return k?.GetValue(name) is int v ? v : null;
    }

    protected override (CheckState, string) Read()
    {
        var v = Value() ?? whenMissing;
        return (v == optimal ? CheckState.Good : CheckState.Warn, describe(v));
    }

    protected override string? Capture() => Value()?.ToString();

    protected override void Apply()
    {
        using var k = Registry.CurrentUser.CreateSubKey(key);
        k.SetValue(name, optimal, RegistryValueKind.DWord);
    }

    protected override void Restore(string? original)
    {
        using var k = Registry.CurrentUser.CreateSubKey(key);
        if (original is null) k.DeleteValue(name, throwOnMissingValue: false);
        else k.SetValue(name, int.Parse(original), RegistryValueKind.DWord);
    }
}

/// <summary>Windows per-app graphics preference (Settings › Display › Graphics).</summary>
public sealed class GpuPreferenceTweak(Settings settings, string appName, string exePath)
    : Tweak("gpu-pref:" + exePath.ToLowerInvariant(), settings, $"{appName} graphics chip",
        $"Runs {appName} (and browser games in it) on the NVIDIA GPU instead of the integrated Intel graphics. Restart {appName} to apply.")
{
    private const string Key = @"Software\Microsoft\DirectX\UserGpuPreferences";
    private const string HighPerformance = "GpuPreference=2;";

    public string AppName => appName;
    public string ExePath => exePath;

    /// <summary>The browser is still running from before the last change, so it hasn't picked it up.</summary>
    public bool NeedsRestart => ChangedAt() is { } changed && BrowserRestart.StartedAt(exePath) is { } started && started < changed;

    private DateTime? ChangedAt()
    {
        if (_settings.BrowserGpuChanged.TryGetValue(exePath, out var changed)) return changed;
        // Changed by an older version that didn't record the time: use the registry key's last write
        return CanUndo ? KeyLastWritten() : null;
    }

    private static DateTime? KeyLastWritten()
    {
        using var k = Registry.CurrentUser.OpenSubKey(Key);
        if (k is null || RegQueryInfoKey(k.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, out var fileTime) != 0)
            return null;
        return DateTime.FromFileTime(fileTime);
    }

    [DllImport("advapi32.dll")]
    private static extern int RegQueryInfoKey(Microsoft.Win32.SafeHandles.SafeRegistryHandle key, IntPtr cls, IntPtr clsLen, IntPtr reserved,
        IntPtr subKeys, IntPtr maxSubKeyLen, IntPtr maxClassLen, IntPtr values, IntPtr maxValueNameLen, IntPtr maxValueLen,
        IntPtr securityDescriptor, out long lastWriteTime);

    private string? Value()
    {
        using var k = Registry.CurrentUser.OpenSubKey(Key);
        return k?.GetValue(exePath) as string;
    }

    protected override (CheckState, string) Read()
    {
        var v = Value() ?? "";
        return v.Contains("GpuPreference=2") ? (CheckState.Good, "NVIDIA (high performance)")
             : v.Contains("GpuPreference=1") ? (CheckState.Warn, "Intel (power saving)")
             : (CheckState.Warn, "Windows decides");
    }

    protected override string? Capture() => Value();

    protected override void Apply()
    {
        using var k = Registry.CurrentUser.CreateSubKey(Key);
        k.SetValue(exePath, HighPerformance, RegistryValueKind.String);
        _settings.BrowserGpuChanged[exePath] = DateTime.Now; // saved by Fix()
    }

    protected override void Restore(string? original)
    {
        using var k = Registry.CurrentUser.CreateSubKey(Key);
        if (original is null) k.DeleteValue(exePath, throwOnMissingValue: false);
        else k.SetValue(exePath, original, RegistryValueKind.String);

        // Undone before a restart: the running browser still has the original setting. Saved by Undo().
        if (NeedsRestart) _settings.BrowserGpuChanged.Remove(exePath);
        else _settings.BrowserGpuChanged[exePath] = DateTime.Now;
    }

    public static string? FindExe(string exeName)
    {
        foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            using var k = hive.OpenSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{exeName}");
            if (k?.GetValue(null) is string path && File.Exists(path.Trim('"'))) return path.Trim('"');
        }
        return null;
    }
}

/// <summary>Laptops cap GPU power hard on battery.</summary>
public sealed class PluggedInCheck() : Check("Power source",
    "Gaming laptops cut GPU power sharply on battery. Plug in for full performance.")
{
    protected override (CheckState, string) Read() =>
        System.Windows.Forms.SystemInformation.PowerStatus.PowerLineStatus switch
        {
            System.Windows.Forms.PowerLineStatus.Online => (CheckState.Good, "Plugged in"),
            System.Windows.Forms.PowerLineStatus.Offline => (CheckState.Warn, "On battery"),
            _ => (CheckState.Info, "Unknown"),
        };

    protected override string CurrentActionLabel() => "";
    protected override void RunActionCore() { }
}

/// <summary>The laptop maker's control app (fan curves, Turbo/Performance mode, power limits).</summary>
public sealed class VendorControlCheck : Check
{
    private readonly string _appId;

    private VendorControlCheck(string name, string appId) : base(name,
        $"{name} controls fan speed and the laptop's performance mode, which set how much power the CPU and GPU may draw. " +
        "For gaming, pick Performance/Turbo and fans on Auto or Max.")
    {
        _appId = appId;
    }

    protected override (CheckState, string) Read() => (CheckState.Info, "Set the mode in the app");
    protected override string CurrentActionLabel() => "Open";

    protected override void RunActionCore() =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"shell:AppsFolder\\{_appId}") { UseShellExecute = true });

    /// <summary>Finds Acer NitroSense / PredatorSense via the installed app packages.</summary>
    public static VendorControlCheck? Find()
    {
        const string packages = @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages";
        using var root = Registry.CurrentUser.OpenSubKey(packages);
        if (root is null) return null;

        foreach (var app in new[] { "NitroSense", "PredatorSense" })
        {
            // Full name: Publisher.Name_Version_Arch__PublisherId -> family: Publisher.Name_PublisherId
            var full = root.GetSubKeyNames().FirstOrDefault(n => n.Contains(app, StringComparison.OrdinalIgnoreCase) && n.Contains("__"));
            if (full is null) continue;
            var family = full[..full.IndexOf('_')] + "_" + full[(full.LastIndexOf("__", StringComparison.Ordinal) + 2)..];
            return new VendorControlCheck(app == "NitroSense" ? "Acer NitroSense" : "Acer PredatorSense", $"{family}!{app}");
        }
        return null;
    }
}

public static class Checks
{
    public static List<Check> Create(Settings settings)
    {
        var list = new List<Check> { new PluggedInCheck(), new PowerModeTweak(settings) };

        if (VendorControlCheck.Find() is { } vendor) list.Add(vendor);

        foreach (var (name, exe) in new[] { ("Edge", "msedge.exe"), ("Chrome", "chrome.exe") })
            if (GpuPreferenceTweak.FindExe(exe) is { } path) list.Add(new GpuPreferenceTweak(settings, name, path));

        list.Add(new RegistryDwordTweak("game-mode", settings, "Game Mode",
            "Windows prioritizes the game and holds back Windows Update installs and notifications while you play.",
            @"Software\Microsoft\GameBar", "AutoGameModeEnabled", optimal: 1, whenMissing: 1,
            v => v == 1 ? "On" : "Off"));

        list.Add(new RegistryDwordTweak("bg-recording", settings, "Background recording",
            "Xbox Game Bar's \"record what happened\" keeps encoding video all the time, which costs FPS.",
            @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "HistoricalCaptureEnabled", optimal: 0, whenMissing: 0,
            v => v == 0 ? "Off" : "On"));

        foreach (var c in list) c.Refresh();
        return list;
    }
}
