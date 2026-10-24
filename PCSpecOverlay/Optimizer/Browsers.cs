using System.IO;
using System.Text;
using Microsoft.Win32;
using PCSpecOverlay.Core;

namespace PCSpecOverlay.Optimizer;

public sealed record BrowserInfo(string Name, string ExeName, string ExePath)
{
    public string ProcessName => Path.GetFileNameWithoutExtension(ExeName);
}

/// <summary>Finds supported Chromium browsers and looks at their running windows in this session.</summary>
public static class Browsers
{
    private static readonly (string Name, string Exe, string[] Fallbacks)[] Known =
    [
        ("Edge", "msedge.exe", [@"%ProgramFiles(x86)%\Microsoft\Edge\Application\msedge.exe", @"%ProgramFiles%\Microsoft\Edge\Application\msedge.exe"]),
        ("Chrome", "chrome.exe", [@"%ProgramFiles%\Google\Chrome\Application\chrome.exe", @"%LocalAppData%\Google\Chrome\Application\chrome.exe"]),
    ];

    /// <summary>Installed browsers with their real executable paths (App Paths registration first, then standard locations).</summary>
    public static List<BrowserInfo> Installed()
    {
        var list = new List<BrowserInfo>();
        foreach (var (name, exe, fallbacks) in Known)
        {
            var path = AppPath(exe) ?? fallbacks.Select(Environment.ExpandEnvironmentVariables).FirstOrDefault(File.Exists);
            if (path != null) list.Add(new BrowserInfo(name, exe, path));
        }
        return list;
    }

    private static string? AppPath(string exe)
    {
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            using var k = hive.OpenSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{exe}");
            if (k?.GetValue(null) is string path && File.Exists(path.Trim('"'))) return path.Trim('"');
        }
        return null;
    }

    /// <summary>When this browser started in the current session, or null if it isn't running here.</summary>
    public static DateTime? StartedUtc(BrowserInfo b)
    {
        DateTime? oldest = null;
        foreach (var p in ProcessUtil.ByName(b.ProcessName))
            using (p)
                if (ProcessUtil.StartUtc(p) is { } s && (oldest is null || s < oldest)) oldest = s;
        return oldest;
    }

    /// <summary>Visible, titled top-level browser windows in this session.</summary>
    public static List<(IntPtr Handle, string Title)> Windows(BrowserInfo b)
    {
        var pids = ProcessUtil.ByName(b.ProcessName).Select(p => { using (p) return p.Id; }).ToHashSet();
        var found = new List<(IntPtr, string)>();
        var cls = new StringBuilder(64);
        Native.EnumWindows((hwnd, _) =>
        {
            Native.GetWindowThreadProcessId(hwnd, out var pid);
            if (!pids.Contains((int)pid) || !Native.IsWindowVisible(hwnd) || Native.GetWindowTextLength(hwnd) == 0) return true;
            cls.Clear();
            Native.GetClassName(hwnd, cls, cls.Capacity);
            if (cls.ToString() == "Chrome_WidgetWin_1") found.Add((hwnd, Native.WindowTitle(hwnd)));
            return true;
        }, IntPtr.Zero);
        return found;
    }

    /// <summary>
    /// Distinct profile names shown in window titles ("page - Profile - Microsoft Edge").
    /// Chromium only adds the profile to the title when several profiles exist, so this can undercount.
    /// </summary>
    public static int OpenProfileCount(BrowserInfo b) =>
        Windows(b).Select(w => w.Title.Replace("​", "").Split(" - "))
            .Where(parts => parts.Length >= 3)
            .Select(parts => parts[^2])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
}
