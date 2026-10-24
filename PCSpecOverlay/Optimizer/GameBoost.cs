using System.Diagnostics;

namespace PCSpecOverlay.Optimizer;

/// <summary>
/// While a game is in front, raises its CPU priority and lowers the user's chosen background apps
/// (e.g. an AI training run), so the game wins when they compete for CPU time.
/// Every change is undone when boost turns off, the game loses focus, or the overlay exits.
/// This shifts CPU time only; it doesn't change how the GPU is shared.
/// </summary>
internal sealed class GameBoost
{
    private readonly Dictionary<int, ProcessPriorityClass> _original = new();
    private string? _boostedGame;

    public bool Enabled { get; private set; }
    public bool Active => _boostedGame != null;

    public void SetEnabled(bool enabled)
    {
        Enabled = enabled;
        if (!enabled) RestoreAll();
    }

    /// <param name="gameName">Process name of the foreground app if it is rendering like a game, else null.</param>
    public void Tick(string? gameName, IReadOnlyCollection<string> backgroundTargets)
    {
        if (!Enabled) return;

        if (gameName is null)
        {
            RestoreAll();
            return;
        }

        if (!string.Equals(gameName, _boostedGame, StringComparison.OrdinalIgnoreCase))
        {
            RestoreAll(); // a different game came to the front
            _boostedGame = gameName;
        }

        var keep = new HashSet<int>();
        // Only raise the game's Normal processes: browsers park background tabs at Idle on purpose
        SetPriority(gameName, ProcessPriorityClass.AboveNormal, keep, onlyFrom: ProcessPriorityClass.Normal);
        foreach (var target in backgroundTargets)
            if (!string.Equals(target, gameName, StringComparison.OrdinalIgnoreCase))
                SetPriority(target, ProcessPriorityClass.BelowNormal, keep, onlyFrom: null);

        // Targets removed from the list, or processes that exited
        foreach (var pid in _original.Keys.Where(pid => !keep.Contains(pid)).ToList())
            Restore(pid);
    }

    /// <param name="onlyFrom">When set, only processes currently at this priority are changed.</param>
    private void SetPriority(string name, ProcessPriorityClass priority, HashSet<int> keep, ProcessPriorityClass? onlyFrom)
    {
        foreach (var p in Process.GetProcessesByName(name))
        {
            using (p)
            {
                try
                {
                    if (!_original.ContainsKey(p.Id))
                    {
                        if (onlyFrom is { } from && p.PriorityClass != from) continue;
                        _original[p.Id] = p.PriorityClass;
                    }
                    if (p.PriorityClass != priority) p.PriorityClass = priority;
                    keep.Add(p.Id);
                }
                catch { /* access denied or exited */ }
            }
        }
    }

    private void Restore(int pid)
    {
        if (!_original.Remove(pid, out var priority)) return;
        try
        {
            using var p = Process.GetProcessById(pid);
            p.PriorityClass = priority;
        }
        catch { /* exited */ }
    }

    public void RestoreAll()
    {
        foreach (var pid in _original.Keys.ToList()) Restore(pid);
        _boostedGame = null;
    }
}
