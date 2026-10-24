using PCSpecOverlay.Core;

namespace PCSpecOverlay.Optimizer;

/// <summary>
/// Knows whether a running browser still has an old graphics preference. Pure logic over
/// <see cref="AppState"/> so it can be tested; process start times are passed in.
/// </summary>
public static class RestartTracker
{
    /// <summary>Record a preference change from <paramref name="before"/> to <paramref name="after"/>.</summary>
    public static void Changed(AppState state, string exePath, string? before, string? after, DateTime nowUtc)
    {
        if (state.BrowserRestarts.TryGetValue(exePath, out var pending))
        {
            // Changed back to what the running browser already uses: nothing to restart
            if (after == pending.LoadedValue) state.BrowserRestarts.Remove(exePath);
            else pending.ChangedUtc = nowUtc;
        }
        else if (before != after)
        {
            state.BrowserRestarts[exePath] = new PendingRestart { ChangedUtc = nowUtc, LoadedValue = before };
        }
        state.Save();
    }

    /// <summary>
    /// True when the browser is running and started before the latest change. A browser that isn't
    /// running, or that started after the change, already has (or will load) the new value.
    /// </summary>
    public static bool NeedsRestart(AppState state, string exePath, DateTime? browserStartedUtc)
    {
        if (!state.BrowserRestarts.TryGetValue(exePath, out var pending)) return false;
        if (browserStartedUtc is { } started && started < pending.ChangedUtc) return true;
        if (browserStartedUtc is { } restarted && restarted >= pending.ChangedUtc)
        {
            state.BrowserRestarts.Remove(exePath); // picked up the new value
            state.Save();
        }
        return false;
    }
}
