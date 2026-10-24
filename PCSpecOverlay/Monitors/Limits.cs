namespace PCSpecOverlay.Monitors;

/// <summary>Why the CPU or GPU is running slower than it could.</summary>
[Flags]
public enum Limit
{
    None = 0,
    GpuThermal = 1, // slowing down to cool off
    GpuPower = 2,   // hit its power cap
    CpuThermal = 4,
    CpuPower = 8,
}

/// <summary>
/// Smooths per-second limit readings. Throttling flickers on and off between samples, so a limit
/// stays visible for a few seconds after it was last seen, and we track how long it has been going
/// on (with short gaps bridged) to decide when it's worth an alert.
/// </summary>
internal sealed class LimitTracker
{
    private const long HoldMs = 4_000;

    private readonly Dictionary<Limit, long> _lastSeen = new();
    private readonly Dictionary<Limit, long> _since = new();

    /// <returns>Limits seen within the last few seconds.</returns>
    public Limit Update(Limit current)
    {
        var now = Environment.TickCount64;
        var visible = Limit.None;
        foreach (var flag in Enum.GetValues<Limit>().Where(f => f != Limit.None))
        {
            if (current.HasFlag(flag))
            {
                _lastSeen[flag] = now;
                _since.TryAdd(flag, now);
            }
            if (_lastSeen.TryGetValue(flag, out var seen) && now - seen <= HoldMs) visible |= flag;
            else _since.Remove(flag);
        }
        return visible;
    }

    /// <summary>How long a limit has been going on without a long break.</summary>
    public TimeSpan ActiveFor(Limit flag) =>
        _since.TryGetValue(flag, out var since) ? TimeSpan.FromMilliseconds(Environment.TickCount64 - since) : TimeSpan.Zero;
}
