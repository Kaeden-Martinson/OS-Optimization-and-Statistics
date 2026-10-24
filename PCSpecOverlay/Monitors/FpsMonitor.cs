using System.Runtime.InteropServices;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;
using PCSpecOverlay.Core;

namespace PCSpecOverlay.Monitors;

/// <summary>
/// Counts presented frames via ETW, the same signals PresentMon / vendor overlays use.
/// <list type="bullet">
/// <item><b>DirectX</b> (DXGI for D3D10–12 and browsers, plus Direct3D 9): counted per swap chain. Per swap chain
/// matters for browsers, whose one GPU process presents every window, tab and video.</item>
/// <item><b>Vulkan, OpenGL and anything else</b>: counted from the graphics kernel's present event
/// (Microsoft-Windows-DxgKrnl, Present_Info, "Present" keyword), per window. DirectX apps emit this too,
/// so it's only used for apps that send no DirectX presents — nothing is counted twice.</item>
/// </list>
/// Opening a real-time ETW session needs administrator rights or membership in the
/// "Performance Log Users" group. See <see cref="FrameStats"/> for the calculations; an app that
/// hasn't presented for 2 s (paused, minimized) reads as unavailable rather than 0. Windows delivers
/// real-time events in batches about once a second, so that takes up to ~3 s to show.
/// Memory is bounded: 10 s of timestamps per source; sources silent for 30 s are dropped.
/// </summary>
internal sealed class FpsMonitor : IDisposable
{
    private static readonly Guid DxgiProvider = new("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9");
    private static readonly Guid D3D9Provider = new("783ACA0A-790E-4D7F-8451-AA850511C6B9");
    private static readonly Guid DxgKrnlProvider = new("802EC45A-1E99-4B83-9920-87C98277BA9D");
    private const int DxgiPresentStart = 42, DxgiPresentMpoStart = 55, D3D9PresentStart = 1, KernelPresentInfo = 184;
    private const ulong EventsKeyword = 0x2;
    // DxgKrnl's "Present" keyword: ~1 event per frame per app. Without it the provider sends tens of
    // thousands of events a second and frames get lost.
    private const ulong KernelPresentKeyword = 0x8000000;

    public static string SessionName => "PCSpecOverlay-Presents" + AppIdentity.Suffix;
    private const long StaleAfterMs = 2_000;   // stop showing FPS once an app stops presenting

    public enum Source { DirectX, Kernel }

    private sealed class History(int pid, Source source, ulong target)
    {
        public readonly int Pid = pid;
        public readonly Source Source = source;
        public readonly ulong Target = target;               // swap chain (DirectX) or window handle (kernel)
        public readonly List<double> Presents = new(2048);   // ETW timestamps, ms
        public long LastSeenTick;                            // Environment.TickCount64 when last received
    }

    private readonly Dictionary<(int Pid, Source Source, ulong Target), History> _histories = new();
    private readonly Dictionary<int, string?> _names = new(); // pid -> process name cache
    private readonly object _lock = new();
    private readonly List<double> _scratch = new(4096);
    private TraceEventSession? _session;

    public bool IsRunning => _session != null;
    public bool KernelPresents { get; private set; }
    public Exception? LastError { get; private set; }

    /// <summary>Where the last sample came from, for diagnostics ("DirectX" / "Vulkan/OpenGL").</summary>
    public Source? LastSource { get; private set; }

    public bool Start()
    {
        try
        {
            _session = new TraceEventSession(SessionName) { StopOnDispose = true };
            // Only the "Events" keyword (present start/stop); the default (all keywords) floods us with object tracking
            _session.EnableProvider(DxgiProvider, TraceEventLevel.Informational, EventsKeyword);
            _session.EnableProvider(D3D9Provider, TraceEventLevel.Informational, EventsKeyword);
            try
            {
                _session.EnableProvider(DxgKrnlProvider, TraceEventLevel.Informational, KernelPresentKeyword);
                KernelPresents = true;
            }
            catch (Exception e)
            {
                Log.Warn("FPS", $"Vulkan/OpenGL frame counting unavailable: {e.Message}");
            }
            _session.Source.AllEvents += OnEvent;

            var session = _session;
            new Thread(() =>
            {
                try { session.Source.Process(); }
                catch { /* session stopped */ }
            }) { IsBackground = true, Name = "ETW presents" }.Start();
            Log.Info("FPS", $"Frame monitoring started (DirectX{(KernelPresents ? ", Vulkan and OpenGL" : " only")})");
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
        Source source;
        ulong target;
        if (e.ProviderGuid == DxgiProvider ? id is DxgiPresentStart or DxgiPresentMpoStart
            : e.ProviderGuid == D3D9Provider && id == D3D9PresentStart)
        {
            // DirectX present-start events begin with the swap chain pointer (read in place, no copy)
            if (e.EventDataLength < e.PointerSize) return;
            source = Source.DirectX;
            target = ReadPointer(e, 0);
        }
        else if (e.ProviderGuid == DxgKrnlProvider && id == KernelPresentInfo)
        {
            // Present_Info: a 32-bit handle, then the window handle
            if (e.EventDataLength < 4 + e.PointerSize) return;
            source = Source.Kernel;
            target = ReadPointer(e, 4);
        }
        else return;

        var t = e.TimeStampRelativeMSec;
        lock (_lock)
        {
            var key = (e.ProcessID, source, target);
            if (!_histories.TryGetValue(key, out var h))
                _histories[key] = h = new History(e.ProcessID, source, target);

            h.Presents.Add(t);
            h.LastSeenTick = Environment.TickCount64;

            // Trim history outside the 1% low window
            var cut = 0;
            while (cut < h.Presents.Count && t - h.Presents[cut] > FrameStats.LowWindowMs) cut++;
            if (cut > 256) h.Presents.RemoveRange(0, cut);
        }
    }

    private static ulong ReadPointer(TraceEvent e, int offset) =>
        e.PointerSize == 8 ? (ulong)Marshal.ReadInt64(e.DataStart + offset) : (uint)Marshal.ReadInt32(e.DataStart + offset);

    /// <param name="processId">The foreground window's process.</param>
    /// <param name="window">The foreground window, to pick the right one when an app has several.</param>
    public FpsSample? Sample(int processId, IntPtr window = default)
    {
        lock (_lock)
        {
            var now = Environment.TickCount64;
            ForgetDeadHistories(now);

            var fresh = _histories.Values.Where(h => h.Presents.Count >= 3 && now - h.LastSeenTick <= StaleAfterMs)
                .Select(h => new Candidate(h.Pid, h.Source, h.Target, FrameStats.RecentCount(h.Presents), h))
                .ToList();
            var pick = Pick(fresh, processId, (ulong)window, ProcessName);

            LastSource = pick?.Source;
            return pick?.History is History chosen ? FrameStats.Compute(chosen.Presents, _scratch) : null;
        }
    }

    /// <summary>Whether this window (kernel) or swap chain (DirectX) of a process presented within the staleness limit.</summary>
    internal bool IsPresenting(int processId, ulong target)
    {
        lock (_lock)
            return _histories.Values.Any(h => h.Pid == processId && h.Target == target &&
                                              Environment.TickCount64 - h.LastSeenTick <= StaleAfterMs);
    }

    /// <summary>A recently presenting source, as seen by <see cref="Pick"/>.</summary>
    internal sealed record Candidate(int Pid, Source Source, ulong Target, int RecentFrames, object? History = null);

    /// <summary>
    /// Chooses whose frames to report for the foreground window. The window's own process always comes
    /// first; another process's frames are only borrowed when it has none at all.
    /// <list type="number">
    /// <item>DirectX from the window's own process (busiest swap chain).</item>
    /// <item>Kernel presents (Vulkan/OpenGL) from the window's own process — this exact window if possible.</item>
    /// <item>DirectX from a same-named helper process: browsers draw in a separate GPU process.</item>
    /// </list>
    /// Borrowing before checking the window's own kernel presents made a Vulkan window report the frame
    /// rate of another, same-named process.
    /// </summary>
    internal static Candidate? Pick(IReadOnlyList<Candidate> fresh, int processId, ulong window, Func<int, string?> nameOf)
    {
        Candidate? Busiest(Func<Candidate, bool> match) =>
            fresh.Where(match).OrderByDescending(c => c.RecentFrames).FirstOrDefault();

        var own = Busiest(c => c.Source == Source.DirectX && c.Pid == processId)
                  ?? Busiest(c => c.Source == Source.Kernel && c.Pid == processId && c.Target == window)
                  ?? Busiest(c => c.Source == Source.Kernel && c.Pid == processId);
        if (own != null) return own;

        var name = nameOf(processId);
        return name is null ? null
            : Busiest(c => c.Source == Source.DirectX && c.Pid != processId && nameOf(c.Pid) == name);
    }

    private string? ProcessName(int pid)
    {
        if (!_names.TryGetValue(pid, out var name))
        {
            name = Native.ProcessName(pid);
            _names[pid] = name;
        }
        return name;
    }

    private void ForgetDeadHistories(long now)
    {
        foreach (var key in _histories.Where(kv => now - kv.Value.LastSeenTick > 30_000).Select(kv => kv.Key).ToList())
            _histories.Remove(key);
        foreach (var pid in _names.Keys.Where(pid => !_histories.Keys.Any(k => k.Pid == pid)).ToList())
            _names.Remove(pid);
    }

    public void Dispose()
    {
        _session?.Dispose();
        _session = null;
    }
}
