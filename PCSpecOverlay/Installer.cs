using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Diagnostics.Tracing.Session;
using Microsoft.Win32;
using PCSpecOverlay.Core;
using PCSpecOverlay.Monitors;
using PCSpecOverlay.Optimizer;

namespace PCSpecOverlay;

/// <summary>
/// Per-user install, update and uninstall — no administrator rights, no terminal.
/// <list type="bullet">
/// <item>Install/update: run the setup exe. It copies itself to %LocalAppData%\Programs\PCSpecOverlay,
/// keeping the previous version as a fallback, adds a Start menu shortcut and an entry in
/// Settings › Apps, and can turn on Start with Windows. Settings in %AppData% are untouched by updates.</item>
/// <item>Uninstall: from Settings › Apps. Optionally undoes Windows changes the app made and deletes settings.
/// Always removes the startup task, shortcut, uninstall entry and monitoring session.</item>
/// </list>
/// </summary>
internal static class Installer
{
    public static string Version => AppIdentity.Version;
    public static bool IsInstalledCopy => AppIdentity.IsInstalledCopy;
    public static string Describe(string? path) => AppIdentity.Redact(path);

    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\PCSpecOverlay";
    private const string ExitEvent = @"Local\PCSpecOverlay.Exit";

    private static string ShortcutPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Programs), $"{AppIdentity.ProductName}.lnk");

    public static string ExitEventName => ExitEvent + AppIdentity.Suffix;

    /// <returns>True if this run was an install/uninstall and the app should exit afterwards.</returns>
    public static bool HandleCommandLine(string[] args)
    {
        if (args.Contains("--uninstall", StringComparer.OrdinalIgnoreCase))
        {
            new InstallerWindow(InstallerWindow.Mode.Uninstall).ShowDialog();
            return true;
        }
        if (AppIdentity.IsSetup || args.Contains("--install", StringComparer.OrdinalIgnoreCase))
        {
            new InstallerWindow(InstallerWindow.Mode.Install).ShowDialog();
            return true;
        }
        return false;
    }

    public static string? InstalledVersion()
    {
        using var k = Registry.CurrentUser.OpenSubKey(UninstallKey);
        return File.Exists(AppIdentity.InstalledExe) ? k?.GetValue("DisplayVersion") as string ?? "unknown" : null;
    }

    /// <summary>Asks a running installed copy to exit (it restores what it changed) and waits for it.</summary>
    public static bool StopRunningCopy()
    {
        if (EventWaitHandle.TryOpenExisting(ExitEvent, out var exit)) using (exit) exit.Set();
        var deadline = Environment.TickCount64 + 15_000;
        while (Environment.TickCount64 < deadline)
        {
            var running = Process.GetProcessesByName("PCSpecOverlay")
                .Where(p => { using (p) return p.Id != Environment.ProcessId && SamePath(SafePath(p), AppIdentity.InstalledExe); }).Count();
            if (running == 0) return true;
            Thread.Sleep(250);
        }
        return false;
    }

    /// <returns>Null on success, otherwise an error for the user.</returns>
    public static string? Install(bool startWithWindows)
    {
        var source = Environment.ProcessPath!;
        var dir = AppIdentity.InstallDirectory;
        var target = AppIdentity.InstalledExe;
        var next = target + ".new";
        var previous = target + ".previous";

        if (!StopRunningCopy()) return "The running copy of the app didn't close. Exit it from the tray icon and try again.";

        try
        {
            Directory.CreateDirectory(dir);
            File.Copy(source, next, overwrite: true);
            if (File.Exists(target)) File.Move(target, previous, overwrite: true);
            try { File.Move(next, target); }
            catch
            {
                if (File.Exists(previous) && !File.Exists(target)) File.Move(previous, target); // roll back
                throw;
            }
        }
        catch (Exception e)
        {
            Log.Error("Install", "Copying the app failed", e);
            return $"Couldn't copy the app to {Describe(dir)}: {e.Message}";
        }

        try
        {
            CreateShortcut(target);
            DeleteLegacyShortcut();
            using (var k = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                k.SetValue("DisplayName", AppIdentity.ProductName);
                k.SetValue("DisplayVersion", Version);
                k.SetValue("Publisher", AppIdentity.ProductName);
                k.SetValue("InstallLocation", dir);
                k.SetValue("DisplayIcon", target);
                k.SetValue("UninstallString", $"\"{target}\" --uninstall");
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                k.SetValue("EstimatedSize", (int)(new FileInfo(target).Length / 1024), RegistryValueKind.DWord);
            }
        }
        catch (Exception e)
        {
            Log.Error("Install", "Creating the shortcut or Apps entry failed", e);
            return $"The app was copied, but the Start menu shortcut or Apps entry couldn't be created: {e.Message}";
        }

        // The installed copy registers its own startup task (so the task points at it), then keeps running
        Process.Start(new ProcessStartInfo(target, startWithWindows ? "--startup-on" : "") { UseShellExecute = true });
        Log.Info("Install", $"Installed {Version} to {Describe(dir)}");
        return null;
    }

    /// <summary>Removes the app. Runs from the installed copy; the folder is deleted after this process exits.</summary>
    public static List<string> Uninstall(bool undoChanges, bool deleteData)
    {
        var report = new List<string>();
        if (!StopRunningCopy()) report.Add("The running app didn't close in time; it will be removed anyway.");

        if (undoChanges) report.AddRange(UndoAllChanges());

        var startup = StartupTask.SetEnabled(false);
        report.Add(startup ?? "Removed Start with Windows.");

        try { TraceEventSession.GetActiveSession(FpsMonitor.SessionName)?.Stop(); } catch { /* not running */ }
        try { File.Delete(ShortcutPath); } catch { /* already gone */ }
        DeleteLegacyShortcut();
        try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, throwOnMissingSubKey: false); } catch { /* already gone */ }

        if (deleteData)
        {
            try { Directory.Delete(AppPaths.DataDirectory, recursive: true); report.Add("Deleted settings and history."); }
            catch (Exception e) { report.Add($"Couldn't delete settings: {e.Message}"); }
        }

        // This exe is inside the install folder: delete the folder once we've exited. The helper waits for
        // this process to end (not a fixed delay: "timeout" doesn't work without a console), then retries briefly.
        var dir = AppIdentity.InstallDirectory.Replace("'", "''");
        var script = $"Wait-Process -Id {Environment.ProcessId} -Timeout 120 -ErrorAction SilentlyContinue; " +
                     $"foreach ($i in 1..10) {{ Remove-Item -LiteralPath '{dir}' -Recurse -Force -ErrorAction SilentlyContinue; " +
                     $"if (-not (Test-Path -LiteralPath '{dir}')) {{ break }}; Start-Sleep -Seconds 1 }}";
        Process.Start(new ProcessStartInfo("powershell.exe",
            $"-NoProfile -NonInteractive -WindowStyle Hidden -Command \"{script}\"")
        { CreateNoWindow = true, UseShellExecute = false });
        report.Add("Removed the app, its Start menu shortcut and its Apps entry.");
        return report;
    }

    /// <summary>Puts back every Windows setting and process priority the app still owns.</summary>
    private static List<string> UndoAllChanges()
    {
        var report = new List<string>();
        var state = AppState.Load();
        var journal = new ChangeJournal(state, TimeProvider.System);
        foreach (var entry in journal.Entries.ToList())
        {
            if (SettingCatalog.Resolve(entry.Id) is not { } setting) continue;
            foreach (var owner in entry.Owners.ToList()) report.Add(journal.Release(setting, owner).Message);
        }
        new GameBoost(state).RestoreAll();
        if (report.Count == 0) report.Add("There were no Windows changes to undo.");
        return report;
    }

    /// <summary>Versions before 2.1.0 named the Start-menu shortcut after the old product name.</summary>
    private static void DeleteLegacyShortcut()
    {
        try { File.Delete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), $"{AppIdentity.LegacyProductName}.lnk")); }
        catch { /* not there */ }
    }

    private static void CreateShortcut(string target)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("Windows Script Host is unavailable");
        dynamic shell = Activator.CreateInstance(shellType)!;
        try
        {
            var link = shell.CreateShortcut(ShortcutPath);
            link.TargetPath = target;
            link.WorkingDirectory = Path.GetDirectoryName(target);
            link.Description = "Performance overlay and optimizer";
            link.IconLocation = target + ",0";
            link.Save();
        }
        finally { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell); }
    }

    private static string? SafePath(Process p)
    {
        try { return p.MainModule?.FileName; } catch { return null; }
    }

    private static bool SamePath(string? a, string b) =>
        a != null && string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
}
