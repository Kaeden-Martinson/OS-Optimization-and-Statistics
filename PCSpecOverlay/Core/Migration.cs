using System.Runtime.InteropServices;
using Microsoft.Win32;
using PCSpecOverlay.Optimizer;

namespace PCSpecOverlay.Core;

/// <summary>Moves undo information from the version-1 settings file into the change journal, once.</summary>
public static class Migration
{
    public static void Run(Settings settings, AppState state, Func<string, IReversibleSetting?> resolve)
    {
        var migrated = false;

        foreach (var (id, original) in settings.TweakBackups ?? new())
        {
            migrated = true;
            if (state.Changes.Any(c => c.Id == id)) continue;
            if (resolve(id) is not { } setting)
            {
                Log.Warn("Migration", $"Skipped undo record for unknown setting '{id}'");
                continue;
            }
            // Version 1 applied the optimal value; whatever is there now is treated as what we applied,
            // so a later Undo still checks that nobody changed it since.
            state.Changes.Add(new JournalEntry
            {
                Id = id, Original = original, Applied = setting.Read(), Owners = [Tweak.Owner],
                AppliedUtc = DateTime.UtcNow,
            });
            if (setting is GpuPreferenceSetting gpu && !state.BrowserRestarts.ContainsKey(gpu.ExePath))
            {
                var changed = settings.BrowserGpuChanged?.GetValueOrDefault(gpu.ExePath) is { } t && t != default
                    ? t.ToUniversalTime()
                    : KeyLastWrittenUtc(GpuPreferenceSetting.Key);
                if (changed is { } c) state.BrowserRestarts[gpu.ExePath] = new PendingRestart { ChangedUtc = c, LoadedValue = original };
            }
        }

        if (settings.GamingRestorePowerMode is { } power && !state.Changes.Any(c => c.Id == SettingCatalog.Power.Id))
        {
            migrated = true;
            state.Changes.Add(new JournalEntry
            {
                Id = SettingCatalog.Power.Id, Original = power, Applied = PowerMode.BestPerformance.ToString(),
                Owners = [GamingMode.Owner], AppliedUtc = DateTime.UtcNow,
            });
        }

        if (!migrated && settings.BrowserGpuChanged is null) return;
        settings.TweakBackups = null;
        settings.BrowserGpuChanged = null;
        settings.GamingRestorePowerMode = null;
        state.Save();
        settings.Save();
        Log.Info("Migration", $"Moved undo records to state.json ({state.Changes.Count} change(s))");
    }

    private static DateTime? KeyLastWrittenUtc(string key)
    {
        using var k = Registry.CurrentUser.OpenSubKey(key);
        if (k is null || RegQueryInfoKey(k.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, out var fileTime) != 0)
            return null;
        return DateTime.FromFileTimeUtc(fileTime);
    }

    [DllImport("advapi32.dll")]
    private static extern int RegQueryInfoKey(Microsoft.Win32.SafeHandles.SafeRegistryHandle key, IntPtr cls, IntPtr clsLen, IntPtr reserved,
        IntPtr subKeys, IntPtr maxSubKeyLen, IntPtr maxClassLen, IntPtr values, IntPtr maxValueNameLen, IntPtr maxValueLen,
        IntPtr securityDescriptor, out long lastWriteTime);
}
