using System.Diagnostics;
using PCSpecOverlay.Core;

namespace PCSpecOverlay.Optimizer;

public sealed record RestartResult(bool Success, string Message);

/// <summary>
/// Restarts a Chromium browser (Edge, Chrome) so it picks up a new graphics preference, keeping every
/// window and tab. Closing windows one by one would only remember the last window, so instead the
/// browser gets the same "session ending" message Windows sends at shutdown: it saves all windows
/// and exits, then <c>--restore-last-session</c> brings them back.
/// <para>Only ever called after the user confirms. Only processes in the current Windows session are touched.</para>
/// </summary>
internal static class BrowserRestart
{
    private const int ExitTimeoutMs = 15_000;
    private const int RelaunchTimeoutMs = 20_000;

    public static Task<RestartResult> RestartAsync(BrowserInfo browser, AppState state) => Task.Run(() =>
    {
        var name = browser.ProcessName;
        var windows = Browsers.Windows(browser);
        if (windows.Count == 0)
            return new RestartResult(false, $"{browser.Name} has no open windows to restart; it will use the new setting next time it starts.");

        var window = windows[0].Handle;
        Native.SendMessageTimeout(window, Native.WM_QUERYENDSESSION, IntPtr.Zero, IntPtr.Zero, Native.SMTO_ABORTIFHUNG, 5000, out _);
        Native.SendMessageTimeout(window, Native.WM_ENDSESSION, new IntPtr(1), IntPtr.Zero, Native.SMTO_ABORTIFHUNG, 10000, out _);

        // Wait for a clean exit; whatever is left (e.g. Edge "startup boost" background processes) has no windows
        if (!WaitUntil(() => Count(name) == 0, ExitTimeoutMs))
        {
            if (Browsers.Windows(browser).Count > 0)
            {
                Log.Warn("Browser", $"{browser.Name} didn't close; left it running");
                return new RestartResult(false, $"{browser.Name} didn't close (it may be showing a \"Leave site?\" prompt). Nothing was forced closed.");
            }
            foreach (var p in ProcessUtil.ByName(name))
                using (p)
                    try { p.Kill(); } catch { /* already gone */ }
        }

        try
        {
            Process.Start(new ProcessStartInfo(browser.ExePath, "--restore-last-session") { UseShellExecute = true });
        }
        catch (Exception e)
        {
            state.BrowserRestartFailedUtc[browser.ExePath] = DateTime.UtcNow;
            state.Save();
            Log.Error("Browser", $"Couldn't start {browser.Name}", e);
            return new RestartResult(false, $"{browser.Name} closed but couldn't be started again: {e.Message}. Open it yourself; your tabs can be restored from its history.");
        }

        if (!WaitUntil(() => Browsers.Windows(browser).Count > 0, RelaunchTimeoutMs))
        {
            state.BrowserRestartFailedUtc[browser.ExePath] = DateTime.UtcNow;
            state.Save();
            Log.Error("Browser", $"{browser.Name} didn't come back after restart");
            return new RestartResult(false, $"{browser.Name} didn't reopen. It may be crashing on start — try opening it yourself. The app won't offer this restart again until {browser.Name} is running.");
        }

        state.BrowserRestartFailedUtc.Remove(browser.ExePath);
        state.Save();
        Log.Info("Browser", $"Restarted {browser.Name} with {windows.Count} window(s)");
        return new RestartResult(true, $"{browser.Name} restarted.");
    });

    private static int Count(string name)
    {
        var list = ProcessUtil.ByName(name);
        foreach (var p in list) p.Dispose();
        return list.Count;
    }

    private static bool WaitUntil(Func<bool> condition, int timeoutMs)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (condition()) return true;
            Thread.Sleep(300);
        }
        return condition();
    }
}
