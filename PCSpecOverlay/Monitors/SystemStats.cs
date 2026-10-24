using System.Diagnostics;
using PCSpecOverlay.Core;

namespace PCSpecOverlay.Monitors;

/// <summary>
/// CPU load, CPU/package temperature (ACPI thermal zones), CPU clock and limits, and RAM usage.
/// Windows' performance counters can take several seconds to open (longer right after sign-in), so
/// they're opened on a background thread; until then those readings are simply unavailable.
/// </summary>
internal sealed class SystemStats : IDisposable
{
    private const string ProcessorInfo = "Processor Information";
    private const double ThermalLimitC = 90; // a capped CPU this hot is cooling off, not saving power

    /// <summary>Opened together, published at once so readers never see a half-built set.</summary>
    private sealed class Counters
    {
        public PerformanceCounter[] Thermal = [];
        public PerformanceCounter? PerfLimit, PerfPercent, BaseMhz;
    }

    private volatile Counters? _counters;
    private long _prevIdle, _prevKernel, _prevUser;

    public bool Ready => _counters != null;
    public int ThermalZones => _counters?.Thermal.Length ?? 0;
    public bool CanDetectLimits => _counters?.PerfLimit != null;

    public SystemStats()
    {
        Native.GetSystemTimes(out _prevIdle, out _prevKernel, out _prevUser);
        Task.Run(Open);
    }

    private void Open()
    {
        var clock = Stopwatch.StartNew();
        var c = new Counters();
        try
        {
            c.PerfLimit = new PerformanceCounter(ProcessorInfo, "% Performance Limit", "_Total", readOnly: true);
            c.PerfPercent = new PerformanceCounter(ProcessorInfo, "% Processor Performance", "_Total", readOnly: true);
            c.BaseMhz = new PerformanceCounter(ProcessorInfo, "Processor Frequency", "_Total", readOnly: true);
            c.PerfPercent.NextValue(); // rate counter: first read only primes it
        }
        catch (Exception e)
        {
            c.PerfLimit = c.PerfPercent = c.BaseMhz = null;
            Log.Warn("CPU", $"CPU clock and slowdown counters unavailable: {e.Message}");
        }
        try
        {
            var category = new PerformanceCounterCategory("Thermal Zone Information");
            c.Thermal = category.GetInstanceNames()
                .Select(n => new PerformanceCounter("Thermal Zone Information", "High Precision Temperature", n, readOnly: true))
                .ToArray();
        }
        catch (Exception e)
        {
            Log.Warn("CPU", $"CPU temperature unavailable: {e.Message}");
        }
        _counters = c;
        Log.Info("Startup", $"CPU counters ready in the background ({clock.ElapsedMilliseconds} ms)");
    }

    /// <summary>Percent busy across all cores since the previous call.</summary>
    public double? CpuLoad()
    {
        if (!Native.GetSystemTimes(out var idle, out var kernel, out var user)) return null;
        long dIdle = idle - _prevIdle, dKernel = kernel - _prevKernel, dUser = user - _prevUser;
        (_prevIdle, _prevKernel, _prevUser) = (idle, kernel, user);
        long total = dKernel + dUser; // kernel time includes idle time
        return total <= 0 ? null : Math.Clamp(100.0 * (total - dIdle) / total, 0, 100);
    }

    /// <summary>Hottest ACPI thermal zone in °C (on laptops this tracks the CPU package).</summary>
    public double? CpuTempC()
    {
        double? hottest = null;
        foreach (var counter in _counters?.Thermal ?? [])
        {
            try
            {
                var c = counter.NextValue() / 10.0 - 273.15; // tenths of Kelvin
                if (c is > 1 and < 125 && (hottest is null || c > hottest)) hottest = c;
            }
            catch { /* zone disappeared */ }
        }
        return hottest;
    }

    /// <summary>Effective CPU clock (base clock × current performance, including turbo), in GHz.</summary>
    public double? CpuClockGhz()
    {
        var c = _counters;
        try { return c?.BaseMhz is null || c.PerfPercent is null ? null : c.BaseMhz.NextValue() * c.PerfPercent.NextValue() / 100 / 1000; }
        catch { return null; }
    }

    /// <summary>
    /// Whether Windows reports the CPU capped below its normal maximum (thermal or power limits).
    /// The cause isn't exposed directly, so a capped CPU at 90 °C+ counts as thermal, otherwise power.
    /// </summary>
    public Limit CpuLimits(double? tempC)
    {
        float limit;
        try { limit = _counters?.PerfLimit?.NextValue() ?? 100; }
        catch { return Limit.None; }
        if (limit >= 95) return Limit.None; // small dips are normal power management
        return tempC >= ThermalLimitC ? Limit.CpuThermal : Limit.CpuPower;
    }

    public (double UsedGb, double TotalGb)? Ram()
    {
        var status = new Native.MEMORYSTATUSEX { dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Native.MEMORYSTATUSEX>() };
        if (!Native.GlobalMemoryStatusEx(ref status)) return null;
        const double gb = 1024.0 * 1024 * 1024;
        return ((status.ullTotalPhys - status.ullAvailPhys) / gb, status.ullTotalPhys / gb);
    }

    public void Dispose()
    {
        if (_counters is not { } c) return;
        foreach (var counter in c.Thermal) counter.Dispose();
        c.PerfLimit?.Dispose();
        c.PerfPercent?.Dispose();
        c.BaseMhz?.Dispose();
    }
}
