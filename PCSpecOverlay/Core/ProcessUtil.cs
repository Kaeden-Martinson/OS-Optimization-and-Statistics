using System.Diagnostics;

namespace PCSpecOverlay.Core;

/// <summary>
/// Process lookups limited to the current user's Windows session, so the app never touches another
/// signed-in user's programs, plus the list of system processes it must never change.
/// </summary>
public static class ProcessUtil
{
    public static readonly int CurrentSession = Process.GetCurrentProcess().SessionId;

    private static readonly HashSet<string> Critical = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Idle", "Registry", "Memory Compression", "smss", "csrss", "wininit", "winlogon", "services",
        "lsass", "lsaiso", "svchost", "dwm", "fontdrvhost", "sihost", "ctfmon", "audiodg", "explorer",
        "SearchHost", "StartMenuExperienceHost", "ShellExperienceHost", "RuntimeBroker", "conhost",
        "MsMpEng", "NisSrv", "SecurityHealthService", "PCSpecOverlay",
    };

    /// <summary>Processes the app must never end or re-prioritize.</summary>
    public static bool IsCritical(string name) => Critical.Contains(name);

    public static bool InCurrentSession(Process p)
    {
        try { return p.SessionId == CurrentSession; }
        catch { return false; }
    }

    /// <summary>Processes with this name in the current session. The caller disposes them.</summary>
    public static List<Process> ByName(string name)
    {
        var mine = new List<Process>();
        foreach (var p in Process.GetProcessesByName(name))
        {
            if (InCurrentSession(p)) mine.Add(p);
            else p.Dispose();
        }
        return mine;
    }

    public static DateTime? StartUtc(Process p)
    {
        try { return p.StartTime.ToUniversalTime(); }
        catch { return null; }
    }
}
