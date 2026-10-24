using System.Diagnostics;

namespace PCSpecOverlay.Monitors;

/// <summary>CPU load, CPU/package temperature (ACPI thermal zones) and RAM usage.</summary>
internal sealed class SystemStats : IDisposable
{
    private const string ProcessorInfo = "Processor Information";
    private const double ThermalLimitC = 90; // a capped CPU this hot is cooling off, not saving power

    private long _prevIdle, _prevKernel, _prevUser;
    private readonly PerformanceCounter[] _thermal;
    private readonly PerformanceCounter? _perfLimit, _perfPercent, _baseMhz;

    public SystemStats()
    {
        Native.GetSystemTimes(out _prevIdle, out _prevKernel, out _prevUser);
        try
        {
            _perfLimit = new PerformanceCounter(ProcessorInfo, "% Performance Limit", "_Total", readOnly: true);
            _perfPercent = new PerformanceCounter(ProcessorInfo, "% Processor Performance", "_Total", readOnly: true);
            _baseMhz = new PerformanceCounter(ProcessorInfo, "Processor Frequency", "_Total", readOnly: true);
            _perfPercent.NextValue(); // rate counter: first read only primes it
        }
        catch
        {
            _perfLimit = _perfPercent = _baseMhz = null;
        }
        try
        {
            var category = new PerformanceCounterCategory("Thermal Zone Information");
            _thermal = category.GetInstanceNames()
                .Select(n => new PerformanceCounter("Thermal Zone Information", "High Precision Temperature", n, readOnly: true))
                .ToArray();
        }
        catch
        {
            _thermal = [];
        }
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
        foreach (var counter in _thermal)
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
        try { return _baseMhz is null || _perfPercent is null ? null : _baseMhz.NextValue() * _perfPercent.NextValue() / 100 / 1000; }
        catch { return null; }
    }

    /// <summary>
    /// Whether Windows reports the CPU capped below its normal maximum (thermal or power limits).
    /// The cause isn't exposed directly, so a capped CPU at 90 °C+ counts as thermal, otherwise power.
    /// </summary>
    public Limit CpuLimits(double? tempC)
    {
        float limit;
        try { limit = _perfLimit?.NextValue() ?? 100; }
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
        foreach (var counter in _thermal) counter.Dispose();
        _perfLimit?.Dispose();
        _perfPercent?.Dispose();
        _baseMhz?.Dispose();
    }
}
