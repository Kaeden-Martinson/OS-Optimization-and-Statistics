using System.Runtime.InteropServices;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;
using PCSpecOverlay.Core;

namespace PCSpecOverlay.Monitors;

/// <summary>
/// Counts presents per swap chain via ETW, the same signal PresentMon / vendor overlays use.
/// Counting per swap chain rather than per process matters for browsers: one GPU process presents
/// every window, tab and video, and adding those together would inflate the game's FPS.
/// Covers DXGI (DirectX 10/11/12 and browsers) and Direct3D 9. Vulkan and OpenGL games are covered only
/// when their driver presents through DXGI; otherwise FPS shows as unavailable for them.
/// Opening a real-time ETW session needs administrator rights or membership in the
/// "Performance Log Users" group. See <see cref="FrameStats"/> for the calculations; an app that
/// hasn't presented for 2 s (paused, minimized) reads as unavailable rather than 0.
/// Memory is bounded: 10 s of timestamps per swap chain; swap chains silent for 30 s are dropped.
/// </summary>
internal sealed class FpsMonitor : IDisposable
{
    private static readonly Guid DxgiProvider = new("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9");
    private static readonly Guid D3D9Provider = new("783ACA0A-790E-4D7F-8451-AA850511C6B9");
    private const int DxgiPresentStart = 42, DxgiPresentMpoStart = 55, D3D9PresentStart = 1;
    private const ulong EventsKeyword = 0x2;

    public static string SessionName => "PCSpecOverlay-Presents" + AppIdentity.Suffix;
    private const long StaleAfterMs = 2_000;   // stop showing FPS once an app stops presenting

    private sealed class History(int pid)
    {
        public readonly int Pid = pid;
        public readonly List<double> Presents = new(2048); // ETW timestamps, ms
        public long LastSeenTick;                           // Environment.TickCount64 when last received
    }

    private readonly Dictionary<(int Pid, ulong SwapChain), History> _bySwapChain = new();
    private readonly Dictionary<int, string?> _names = new(); // pid -> process name cache
    private readonly object _lock = new();
    private readonly List<double> _scratch = new(4096);
    private TraceEventSession? _session;

    public bool IsRunning => _session != null;
    public Exception? LastError { get; private set; }

    public bool Start()
    {
        try
        {
            _session = new TraceEventSession(SessionName) { StopOnDispose = true };
            // Only the "Events" keyword (present start/stop); the default (all keywords) floods us with object tracking
            _session.EnableProvider(DxgiProvider, TraceEventLevel.Informational, EventsKeyword);
            _session.EnableProvider(D3D9Provider, TraceEventLevel.Informational, EventsKeyword);
            _session.Source.AllEvents += OnEvent;

            var session = _session;
            new Thread(() =>
            {
                try { session.Source.Process(); }
                catch { /* session stopped */ }
            }) { IsBackground = true, Name = "ETW presents" }.Start();
            Log.Info("FPS", "Frame monitoring started");
            return true;
        }
        catch (Exception e)
        {
            LastError = e;
            Log.Warn("FPS", e is UnauthorizedAccessException
                ? "Frame monitoring needs administrator rights or the Performance Log Users group; FPS unavailable"
                : $"Frame monitoring couldn't start: {e.Message}");
            _session?.Dispose();
            _session = null;
            return false;
        }
    }

    private void OnEvent(TraceEvent e)
    {
        var id = (int)e.ID;
        var isPresent = e.ProviderGuid == DxgiProvider ? id is DxgiPresentStart or DxgiPresentMpoStart
                      : e.ProviderGuid == D3D9Provider && id == D3D9PresentStart;
        if (!isPresent) return;

        // Every present-start event begins with the swap chain pointer (read in place, no copy)
        if (e.EventDataLength < e.PointerSize) return;
        var swapChain = e.PointerSize == 8 ? (ulong)Marshal.ReadInt64(e.DataStart) : (uint)Marshal.ReadInt32(e.DataStart);

        var t = e.TimeStampRelativeMSec;
        lock (_lock)
        {
            var key = (e.ProcessID, swapChain);
            if (!_bySwapChain.TryGetValue(key, out var h))
                _bySwapChain[key] = h = new History(e.ProcessID);

            h.Presents.Add(t);
            h.LastSeenTick = Environment.TickCount64;

            // Trim history outside the 1% low window
            var cut = 0;
            while (cut < h.Presents.Count && t - h.Presents[cut] > FrameStats.LowWindowMs) cut++;
            if (cut > 256) h.Presents.RemoveRange(0, cut);
        }
    }

    public FpsSample? Sample(int processId)
    {
        lock (_lock)
        {
            var now = Environment.TickCount64;
            ForgetDeadSwapChains(now);

            // The window's own process first. Browsers (and some launchers) present from a helper
            // process, e.g. Edge/Chrome's GPU process, so fall back to same-named processes.
            var h = Busiest(c => c.Pid == processId, now);
            if (h is null && ProcessName(processId) is { } name)
                h = Busiest(c => ProcessName(c.Pid) == name, now);
            return h is null ? null : FrameStats.Compute(h.Presents, _scratch);
        }
    }

    /// <summary>The fresh swap chain with the most presents in the last second: the game, not a video or idle window.</summary>
    private History? Busiest(Func<History, bool> match, long now)
    {
        History? best = null;
        var bestCount = 0;
        foreach (var h in _bySwapChain.Values)
        {
            if (h.Presents.Count < 3 || now - h.LastSeenTick > StaleAfterMs || !match(h)) continue;
            var count = FrameStats.RecentCount(h.Presents);
            if (count > bestCount) (best, bestCount) = (h, count);
        }
        return best;
    }

    private string? ProcessName(int pid)
    {
        if (!_names.TryGetValue(pid, out var name))
        {
            try { using var p = System.Diagnostics.Process.GetProcessById(pid); name = p.ProcessName; }
            catch { name = null; }
            _names[pid] = name;
        }
        return name;
    }

    private void ForgetDeadSwapChains(long now)
    {
        foreach (var key in _bySwapChain.Where(kv => now - kv.Value.LastSeenTick > 30_000).Select(kv => kv.Key).ToList())
            _bySwapChain.Remove(key);
        foreach (var pid in _names.Keys.Where(pid => !_bySwapChain.Keys.Any(k => k.Pid == pid)).ToList())
            _names.Remove(pid);
    }

    public void Dispose()
    {
        _session?.Dispose();
        _session = null;
    }
}
