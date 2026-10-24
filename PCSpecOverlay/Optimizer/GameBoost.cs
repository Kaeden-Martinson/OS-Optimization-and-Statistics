using System.Diagnostics;
using PCSpecOverlay.Core;

namespace PCSpecOverlay.Optimizer;

/// <summary>
/// While a game is in front, raises its CPU priority one step (Normal → Above normal) and lowers the
/// user's chosen background apps one step (→ Below normal), so the game wins when they compete for
/// CPU time. Conservative by design: never High/Realtime, never system processes, only this session.
/// Every change is recorded in <see cref="AppState"/> (pid + start time) so it can be undone after a
/// crash, and is undone when boost turns off, the game loses focus, or the app exits.
/// This shifts CPU time only; it doesn't change how the GPU is shared.
/// </summary>
internal sealed class GameBoost(AppState state)
{
    private string? _boostedGame;

    public bool Enabled { get; private set; }
    public bool Active => _boostedGame != null;
    public int ChangedCount => state.Priorities.Count;

    public void SetEnabled(bool enabled)
    {
        Enabled = enabled;
        if (!enabled) RestoreAll();
    }

    /// <param name="gameName">Process name of the game in front, or null.</param>
    public void Tick(string? gameName, IReadOnlyCollection<string> backgroundTargets)
    {
        if (!Enabled || gameName is null)
        {
            if (state.Priorities.Count > 0 || _boostedGame != null) RestoreAll();
            return;
        }

        if (!string.Equals(gameName, _boostedGame, StringComparison.OrdinalIgnoreCase))
        {
            RestoreAll(); // a different game came to the front
            _boostedGame = gameName;
        }

        var keep = new HashSet<int>();
        // Only the game's Normal processes: browsers park background tabs at Idle on purpose
        SetPriority(gameName, ProcessPriorityClass.AboveNormal, keep, onlyFrom: ProcessPriorityClass.Normal);
        foreach (var target in backgroundTargets)
            if (!string.Equals(target, gameName, StringComparison.OrdinalIgnoreCase))
                SetPriority(target, ProcessPriorityClass.BelowNormal, keep, onlyFrom: null);

        // Targets removed from the list, or processes that exited
        foreach (var change in state.Priorities.Where(c => !keep.Contains(c.Pid)).ToList())
            Restore(change);
    }

    private void SetPriority(string name, ProcessPriorityClass priority, HashSet<int> keep, ProcessPriorityClass? onlyFrom)
    {
        if (ProcessUtil.IsCritical(name)) return;
        var changed = false;
        foreach (var p in ProcessUtil.ByName(name))
        {
            using (p)
            {
                try
                {
                    var record = state.Priorities.FirstOrDefault(c => c.Pid == p.Id);
                    if (record is null)
                    {
                        var current = p.PriorityClass;
                        if (onlyFrom is { } from && current != from) continue;
                        if (current == priority) continue;
                        if (ProcessUtil.StartUtc(p) is not { } start) continue;
                        record = new PriorityChange
                        {
                            Pid = p.Id, StartUtc = start, Name = name,
                            Original = current.ToString(), Applied = priority.ToString(),
                        };
                        state.Priorities.Add(record);
                        changed = true;
                    }
                    if (p.PriorityClass != priority) p.PriorityClass = priority;
                    keep.Add(p.Id);
                }
                catch { /* access denied or exited */ }
            }
        }
        if (changed) state.Save();
    }

    private void Restore(PriorityChange change)
    {
        state.Priorities.Remove(change);
        try
        {
            using var p = Process.GetProcessById(change.Pid);
            // Same process (not a reused pid), still ours, and nobody changed it since
            if (ProcessUtil.InCurrentSession(p) &&
                ProcessUtil.StartUtc(p) is { } start && Math.Abs((start - change.StartUtc).TotalSeconds) < 1 &&
                p.PriorityClass.ToString() == change.Applied &&
                Enum.TryParse<ProcessPriorityClass>(change.Original, out var original))
                p.PriorityClass = original;
        }
        catch { /* exited */ }
    }

    public void RestoreAll()
    {
        foreach (var change in state.Priorities.ToList()) Restore(change);
        state.Save();
        _boostedGame = null;
    }

    /// <summary>At startup: undo priority changes left behind by a crash.</summary>
    public int RecoverAfterCrash()
    {
        var count = state.Priorities.Count;
        if (count > 0)
        {
            RestoreAll();
            Log.Info("GameBoost", $"Restored {count} process priorit{(count == 1 ? "y" : "ies")} left from the last run");
        }
        return count;
    }
}
