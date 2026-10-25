namespace PCSpecOverlay.Core;

/// <summary>
/// Lets an ongoing problem (a slowdown, a high temperature) be written to the log once per episode
/// instead of on every reading. Call <see cref="IsNew"/> while the condition is active; an episode
/// ends once the condition has been gone for <see cref="ResetAfter"/>.
/// </summary>
public sealed class OncePerEpisode(TimeProvider? time = null)
{
    public static readonly TimeSpan ResetAfter = TimeSpan.FromMinutes(30);

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly Dictionary<string, long> _lastSeen = new();

    /// <summary>True the first time a condition is seen in an episode.</summary>
    public bool IsNew(string key)
    {
        var now = _time.GetTimestamp();
        var isNew = !_lastSeen.TryGetValue(key, out var seen) || _time.GetElapsedTime(seen, now) > ResetAfter;
        _lastSeen[key] = now;
        return isNew;
    }
}
