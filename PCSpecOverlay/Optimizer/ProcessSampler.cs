using System.Diagnostics;

namespace PCSpecOverlay.Optimizer;

public sealed record AppUsage(string Name, double CpuPercent, double GpuPercent, double RamMb, int ProcessCount);

/// <summary>
/// Per-app CPU, GPU and RAM, grouped by process name (so Edge's many processes show as one row).
/// Each call compares against the previous one, so call it on a steady interval.
/// </summary>
internal sealed class ProcessSampler
{
    private Dictionary<int, TimeSpan> _prevCpu = new();
    private DateTime _prevTime = DateTime.UtcNow;
    private Dictionary<string, CounterSample> _prevGpu = new();
    private readonly PerformanceCounterCategory? _gpuEngine;

    public ProcessSampler()
    {
        try { _gpuEngine = new PerformanceCounterCategory("GPU Engine"); }
        catch { _gpuEngine = null; }
        Sample(); // prime the deltas
    }

    public List<AppUsage> Sample()
    {
        var now = DateTime.UtcNow;
        var elapsed = (now - _prevTime).TotalMilliseconds * Environment.ProcessorCount;
        _prevTime = now;

        var gpuByPid = SampleGpu();
        var cpu = new Dictionary<int, TimeSpan>();
        var apps = new Dictionary<string, (double Cpu, double Gpu, double Ram, int Count)>(StringComparer.OrdinalIgnoreCase);

        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                if (p.Id is 0 or 4 || !PCSpecOverlay.Core.ProcessUtil.InCurrentSession(p)) continue; // Idle, System, other users
                double cpuPct = 0;
                try
                {
                    var total = p.TotalProcessorTime;
                    cpu[p.Id] = total;
                    if (elapsed > 0 && _prevCpu.TryGetValue(p.Id, out var before))
                        cpuPct = (total - before).TotalMilliseconds / elapsed * 100;
                }
                catch { /* protected process: no CPU time without admin */ }

                double ram;
                try { ram = p.WorkingSet64 / (1024.0 * 1024); }
                catch { continue; }

                gpuByPid.TryGetValue(p.Id, out var gpu);
                var a = apps.GetValueOrDefault(p.ProcessName);
                apps[p.ProcessName] = (a.Cpu + cpuPct, Math.Max(a.Gpu, gpu), a.Ram + ram, a.Count + 1);
            }
        }
        _prevCpu = cpu;

        return apps.Select(kv => new AppUsage(kv.Key, Math.Min(kv.Value.Cpu, 100), Math.Min(kv.Value.Gpu, 100), kv.Value.Ram, kv.Value.Count))
                   .ToList();
    }

    /// <summary>Busiest GPU engine per process, as Task Manager reports it.</summary>
    private Dictionary<int, double> SampleGpu()
    {
        var result = new Dictionary<int, double>();
        if (_gpuEngine is null) return result;

        var current = new Dictionary<string, CounterSample>();
        try
        {
            var data = _gpuEngine.ReadCategory()["Utilization Percentage"];
            if (data is null) return result;
            foreach (InstanceData d in data.Values)
            {
                current[d.InstanceName] = d.Sample;
                if (!_prevGpu.TryGetValue(d.InstanceName, out var before)) continue;

                // Instance names look like: pid_1234_luid_0x..._phys_0_eng_0_engtype_3D
                var parts = d.InstanceName.Split('_');
                if (parts.Length < 2 || !int.TryParse(parts[1], out var pid)) continue;

                var pct = CounterSample.Calculate(before, d.Sample);
                if (pct > result.GetValueOrDefault(pid)) result[pid] = pct;
            }
        }
        catch { /* counters unavailable */ }

        _prevGpu = current;
        return result;
    }
}
