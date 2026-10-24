namespace PCSpecOverlay.Core;

/// <summary>
/// Decides when an ongoing problem deserves another notification. The first few use the user's
/// cooldown; after that, reminders slow to at most hourly while the problem continues, so a long
/// AI-training night doesn't produce a notification every ten minutes. Once the problem has been
/// gone for a while it counts as resolved, and a new occurrence notifies normally again.
/// </summary>
public sealed class AlertThrottle(TimeProvider? time = null)
{
    public const int RepeatsBeforeBackoff = 3;
    public static readonly TimeSpan Backoff = TimeSpan.FromHours(1);
    public static readonly TimeSpan ResetAfter = TimeSpan.FromMinutes(30);

    private sealed class Episode
    {
        public long LastSeen, LastShown;
        public int Shown;
    }

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly Dictionary<string, Episode> _episodes = new();

    /// <summary>Call while the condition is active. True when a notification should be shown now.</summary>
    /// <param name="shown">How many notifications this episode has had, including this one.</param>
    public bool ShouldNotify(string key, TimeSpan cooldown, out int shown)
    {
        var now = _time.GetTimestamp();
        if (!_episodes.TryGetValue(key, out var e) || _time.GetElapsedTime(e.LastSeen, now) > ResetAfter)
            _episodes[key] = e = new Episode();
        e.LastSeen = now;

        var interval = e.Shown >= RepeatsBeforeBackoff ? (cooldown > Backoff ? cooldown : Backoff) : cooldown;
        if (e.Shown > 0 && _time.GetElapsedTime(e.LastShown, now) < interval)
        {
            shown = e.Shown;
            return false;
        }
        e.Shown++;
        e.LastShown = now;
        shown = e.Shown;
        return true;
    }
}
