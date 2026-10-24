using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using PCSpecOverlay.Core;

namespace PCSpecOverlay.Optimizer;

/// <summary>Windows power mode (Settings › System › Power › Power mode), which sits on top of the active power plan.</summary>
public static class PowerMode
{
    public static readonly Guid BestPerformance = new("ded574b5-45a0-4f42-8737-46345c09c238");
    public static readonly Guid Efficiency = new("961cc777-2547-4f9d-8174-7d86181b8a7a");
    public static readonly Guid Recommended = Guid.Empty; // the slider's middle ("Balanced") position

    [DllImport("powrprof.dll")] private static extern uint PowerGetEffectiveOverlayScheme(out Guid scheme);
    [DllImport("powrprof.dll")] private static extern uint PowerSetActiveOverlayScheme(Guid scheme);
    [DllImport("powrprof.dll")] private static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr scheme);
    [DllImport("powrprof.dll", CharSet = CharSet.Unicode)]
    private static extern uint PowerReadFriendlyName(IntPtr root, ref Guid scheme, IntPtr sub, IntPtr setting, IntPtr buffer, ref uint size);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr mem);

    /// <summary>Current power mode, or null if Windows won't report one (e.g. a plan without power modes).</summary>
    public static Guid? Current() => PowerGetEffectiveOverlayScheme(out var g) == 0 ? g : null;

    public static void Set(Guid g)
    {
        var rc = PowerSetActiveOverlayScheme(g);
        if (rc != 0) throw new InvalidOperationException($"Windows refused the power mode change (error {rc})");
    }

    public static string Name(Guid? g) =>
        g is null ? "Unavailable"
        : g == BestPerformance ? "Best performance"
        : g == Efficiency ? "Best power efficiency"
        : g == Recommended ? "Balanced"
        : "Custom";

    /// <summary>Friendly name of the active power plan (e.g. "Acer"), which the power mode sits on.</summary>
    public static string? PlanName()
    {
        if (PowerGetActiveScheme(IntPtr.Zero, out var ptr) != 0) return null;
        try
        {
            var plan = Marshal.PtrToStructure<Guid>(ptr);
            uint size = 0;
            PowerReadFriendlyName(IntPtr.Zero, ref plan, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, ref size);
            if (size == 0) return null;
            var buffer = Marshal.AllocHGlobal((int)size);
            try
            {
                return PowerReadFriendlyName(IntPtr.Zero, ref plan, IntPtr.Zero, IntPtr.Zero, buffer, ref size) == 0
                    ? Marshal.PtrToStringUni(buffer)
                    : null;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        finally { LocalFree(ptr); }
    }
}

public sealed class PowerModeSetting : IReversibleSetting
{
    public string Id => "power-mode";
    public string Name => "Power mode";
    public string? Unsupported => PowerMode.Current() is null ? "Windows doesn't offer power modes with the current power plan" : null;
    public string? Read() => PowerMode.Current()?.ToString();

    public void Write(string? value)
    {
        // Never fall back to a default: an unreadable stored value means we don't know what to restore
        if (!Guid.TryParse(value, out var g)) throw new ArgumentException($"'{value}' isn't a power mode");
        PowerMode.Set(g);
    }
}

/// <summary>A per-user DWORD value (HKCU only; the app never writes machine-wide settings).</summary>
public sealed class RegistryDwordSetting(string id, string name, string key, string valueName) : IReversibleSetting
{
    public string Id => id;
    public string Name => name;
    public string? Unsupported => null;

    public string? Read()
    {
        using var k = Registry.CurrentUser.OpenSubKey(key);
        return k?.GetValue(valueName) is int v ? v.ToString() : null;
    }

    public void Write(string? value)
    {
        using var k = Registry.CurrentUser.CreateSubKey(key);
        if (value is null) k.DeleteValue(valueName, throwOnMissingValue: false);
        else k.SetValue(valueName, int.Parse(value), RegistryValueKind.DWord);
    }
}

/// <summary>Windows per-app graphics preference (Settings › Display › Graphics), stored per exe path in HKCU.</summary>
public sealed class GpuPreferenceSetting(string appName, string exePath) : IReversibleSetting
{
    public const string Key = @"Software\Microsoft\DirectX\UserGpuPreferences";
    public const string HighPerformance = "GpuPreference=2;";

    public string AppName => appName;
    public string ExePath => exePath;
    public string Id => IdFor(exePath);
    public string Name => $"{appName} graphics preference";
    public string? Unsupported => File.Exists(exePath) ? null : $"{appName} isn't installed at {exePath}";

    public static string IdFor(string exePath) => "gpu-pref:" + exePath.ToLowerInvariant();

    public string? Read()
    {
        using var k = Registry.CurrentUser.OpenSubKey(Key);
        return k?.GetValue(exePath) as string;
    }

    public void Write(string? value)
    {
        using var k = Registry.CurrentUser.CreateSubKey(Key);
        if (value is null) k.DeleteValue(exePath, throwOnMissingValue: false);
        else k.SetValue(exePath, value, RegistryValueKind.String);
    }

    public static string Describe(string? value) =>
        value?.Contains("GpuPreference=2") == true ? "NVIDIA (high performance)"
        : value?.Contains("GpuPreference=1") == true ? "Integrated (power saving)"
        : "Let Windows decide";
}

/// <summary>Every setting the app knows how to change, so journal entries can be resolved after a restart.</summary>
public static class SettingCatalog
{
    public static readonly PowerModeSetting Power = new();

    public static readonly RegistryDwordSetting GameMode = new("game-mode", "Game Mode",
        @"Software\Microsoft\GameBar", "AutoGameModeEnabled");

    public static readonly RegistryDwordSetting BackgroundRecording = new("bg-recording", "Background recording",
        @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "HistoricalCaptureEnabled");

    public static IReversibleSetting? Resolve(string id)
    {
        if (id == Power.Id) return Power;
        if (id == GameMode.Id) return GameMode;
        if (id == BackgroundRecording.Id) return BackgroundRecording;
        if (id.StartsWith("gpu-pref:", StringComparison.Ordinal))
        {
            var browser = Browsers.Installed().FirstOrDefault(b => GpuPreferenceSetting.IdFor(b.ExePath) == id);
            return browser is null ? null : new GpuPreferenceSetting(browser.Name, browser.ExePath);
        }
        return null;
    }
}
