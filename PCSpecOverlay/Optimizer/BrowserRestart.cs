using System.Diagnostics;
using System.IO;
using System.Text;

namespace PCSpecOverlay.Optimizer;

/// <summary>
/// Restarts a Chromium browser (Edge, Chrome) so it picks up a new graphics preference, keeping every
/// window and tab. Closing windows one by one would only remember the last window, so instead the
/// browser gets the same "session ending" message Windows sends at shutdown: it saves all windows
/// and exits, then <c>--restore-last-session</c> brings them back.
/// </summary>
internal static class BrowserRestart
{
    private static string ProcessName(string exePath) => Path.GetFileNameWithoutExtension(exePath);

    /// <summary>When the running browser started, or null if it isn't running.</summary>
    public static DateTime? StartedAt(string exePath)
    {
        DateTime? oldest = null;
        foreach (var p in Process.GetProcessesByName(ProcessName(exePath)))
        {
            using (p)
            {
                try { if (oldest is null || p.StartTime < oldest) oldest = p.StartTime; }
                catch { /* exited */ }
            }
        }
        return oldest;
    }

    public static async Task RestartAsync(string exePath)
    {
        var name = ProcessName(exePath);
        await Task.Run(() =>
        {
            var pids = Process.GetProcessesByName(name).Select(p => { using (p) return p.Id; }).ToHashSet();
            if (BrowserWindow(pids) is { } window)
            {
                Native.SendMessageTimeout(window, Native.WM_QUERYENDSESSION, IntPtr.Zero, IntPtr.Zero, Native.SMTO_ABORTIFHUNG, 5000, out _);
                Native.SendMessageTimeout(window, Native.WM_ENDSESSION, new IntPtr(1), IntPtr.Zero, Native.SMTO_ABORTIFHUNG, 10000, out _);
            }

            // Wait for a clean exit; whatever is left (e.g. Edge "startup boost" background processes) has no windows
            var deadline = Environment.TickCount64 + 15_000;
            while (Process.GetProcessesByName(name) is { Length: > 0 } left && Environment.TickCount64 < deadline)
            {
                foreach (var p in left) p.Dispose();
                Thread.Sleep(300);
            }
            foreach (var p in Process.GetProcessesByName(name))
            {
                using (p)
                {
                    try { p.Kill(); } catch { /* already gone */ }
                }
            }

            Process.Start(new ProcessStartInfo(exePath, "--restore-last-session") { UseShellExecute = true });
        });
    }

    /// <summary>A visible, titled top-level browser window (not a popup bubble or helper window).</summary>
    private static IntPtr? BrowserWindow(HashSet<int> pids)
    {
        IntPtr? found = null;
        var cls = new StringBuilder(64);
        Native.EnumWindows((hwnd, _) =>
        {
            Native.GetWindowThreadProcessId(hwnd, out var pid);
            if (!pids.Contains((int)pid) || !Native.IsWindowVisible(hwnd) || Native.GetWindowTextLength(hwnd) == 0) return true;
            cls.Clear();
            Native.GetClassName(hwnd, cls, cls.Capacity);
            if (cls.ToString() != "Chrome_WidgetWin_1") return true;
            found = hwnd;
            return false;
        }, IntPtr.Zero);
        return found;
    }
}
