using System.IO;
using PCSpecOverlay.Monitors;

namespace PCSpecOverlay.Core;

public readonly record struct HistoryPoint(DateTime Time, double? Fps, double? FrameTimeMs, double? GpuLoad, double? CpuLoad, double? GpuTempC, double? CpuTempC);

/// <summary>Summary of one gaming session, written once when it ends.</summary>
public sealed class SessionSummary
{
    public string Game { get; set; } = "";
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
    public double? AvgFps { get; set; }
    public double? LowestOnePercentLow { get; set; }
    public double? AvgFrameTimeMs { get; set; }
    public double? MaxGpuTempC { get; set; }
    public double? MaxCpuTempC { get; set; }
    public double? AvgGpuLoad { get; set; }
    public double? AvgCpuLoad { get; set; }
    /// <summary>Seconds during which the GPU or CPU reported slowing down from heat.</summary>
    public int ThermalSlowdownSeconds { get; set; }
    public int Samples { get; set; }

    public TimeSpan Duration => EndUtc - StartUtc;
}

/// <summary>
/// Keeps the last 10 minutes of readings in memory for graphs, and summarizes gaming sessions.
/// Disk writes are limited to one small JSON file per finished session; files older than the
/// retention period are deleted. Pure logic apart from file I/O, so it can be tested.
/// </summary>
public sealed class HistoryRecorder(Settings settings, TimeProvider? time = null)
{
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan SessionGap = TimeSpan.FromSeconds(60); // brief alt-tabs don't split a session

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly LinkedList<HistoryPoint> _points = new();
    private SessionAccumulator? _session;
    private DateTime _lastGameSeen;
    private DateTime _lastSample;

    public IReadOnlyCollection<HistoryPoint> Points => _points;
    public SessionSummary? LastSession { get; private set; }
    public string? CurrentGame => _session?.Game;

    public void Record(Telemetry t, string? game)
    {
        if (!settings.HistoryEnabled) return;
        var now = _time.GetUtcNow().UtcDateTime;
        var seconds = _lastSample == default ? 0 : Math.Min(5, (now - _lastSample).TotalSeconds);
        _lastSample = now;

        _points.AddLast(new HistoryPoint(now, t.Fps?.Fps, t.Fps?.FrameTimeMs, t.Gpu.Load, t.CpuLoad, t.Gpu.TempC, t.CpuTempC));
        while (_points.First is { } first && now - first.Value.Time > Window) _points.RemoveFirst();

        if (game != null)
        {
            if (_session != null && _session.Game != game) EndSession();
            _session ??= new SessionAccumulator(game, now);
            _session.Add(t, seconds);
            _lastGameSeen = now;
        }
        else if (_session != null && now - _lastGameSeen > SessionGap)
        {
            EndSession();
        }
    }

    /// <summary>Ends any open session (on exit).</summary>
    public void Flush()
    {
        if (_session != null) EndSession();
    }

    private void EndSession()
    {
        var summary = _session!.Finish(_lastGameSeen);
        _session = null;
        if (summary.Samples < 10) return; // too short to be meaningful
        LastSession = summary;
        Save(summary);
        Prune();
    }

    private void Save(SessionSummary s)
    {
        var name = $"{s.StartUtc:yyyyMMdd-HHmmss}-{string.Concat(s.Game.Where(char.IsLetterOrDigit))}.json";
        JsonFile.Save(Path.Combine(AppPaths.HistoryDirectory, name), s, "History");
    }

    private void Prune()
    {
        try
        {
            var cutoff = DateTime.UtcNow.AddDays(-settings.HistoryRetentionDays);
            foreach (var f in new DirectoryInfo(AppPaths.HistoryDirectory).GetFiles("*.json"))
                if (f.LastWriteTimeUtc < cutoff) f.Delete();
        }
        catch (Exception e) { Log.Warn("History", $"Couldn't prune old sessions: {e.Message}"); }
    }

    /// <summary>Saved sessions, newest first.</summary>
    public static List<SessionSummary> LoadSessions(int max = 20)
    {
        try
        {
            var dir = new DirectoryInfo(AppPaths.HistoryDirectory);
            if (!dir.Exists) return new();
            return dir.GetFiles("*.json").OrderByDescending(f => f.Name).Take(max)
                .Select(f => JsonFile.Load<SessionSummary>(f.FullName, "History"))
                .Where(s => s.Samples > 0).ToList();
        }
        catch { return new(); }
    }

    private sealed class SessionAccumulator(string game, DateTime start)
    {
        public string Game { get; } = game;
        private int _samples, _fpsCount, _gpuCount, _cpuCount;
        private double _fpsSum, _frameSum, _gpuSum, _cpuSum, _throttle;
        private double? _low, _maxGpu, _maxCpu;

        public void Add(Telemetry t, double seconds)
        {
            _samples++;
            if (t.Fps is { } f)
            {
                _fpsSum += f.Fps; _frameSum += f.FrameTimeMs; _fpsCount++;
                _low = _low is null ? f.OnePercentLow : Math.Min(_low.Value, f.OnePercentLow);
            }
            if (t.Gpu.Load is { } g) { _gpuSum += g; _gpuCount++; }
            if (t.CpuLoad is { } c) { _cpuSum += c; _cpuCount++; }
            if (t.Gpu.TempC is { } gt) _maxGpu = Math.Max(_maxGpu ?? gt, gt);
            if (t.CpuTempC is { } ct) _maxCpu = Math.Max(_maxCpu ?? ct, ct);
            if ((t.Limits & (Limit.GpuThermal | Limit.CpuThermal)) != 0) _throttle += seconds;
        }

        public SessionSummary Finish(DateTime end) => new()
        {
            Game = Game, StartUtc = start, EndUtc = end, Samples = _samples,
            AvgFps = _fpsCount > 0 ? _fpsSum / _fpsCount : null,
            AvgFrameTimeMs = _fpsCount > 0 ? _frameSum / _fpsCount : null,
            LowestOnePercentLow = _low,
            AvgGpuLoad = _gpuCount > 0 ? _gpuSum / _gpuCount : null,
            AvgCpuLoad = _cpuCount > 0 ? _cpuSum / _cpuCount : null,
            MaxGpuTempC = _maxGpu, MaxCpuTempC = _maxCpu,
            ThermalSlowdownSeconds = (int)_throttle,
        };
    }
}
