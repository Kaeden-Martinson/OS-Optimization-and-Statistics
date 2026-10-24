using System.Runtime.InteropServices;
using System.Text;
using PCSpecOverlay.Core;

namespace PCSpecOverlay.Monitors;

public enum GpuStatus
{
    Ok,
    /// <summary>No NVIDIA GPU or driver on this PC.</summary>
    NotPresent,
    /// <summary>The laptop GPU is powered down; not woken just to read sensors.</summary>
    Sleeping,
    /// <summary>The driver stopped answering (e.g. driver update or reset); retrying.</summary>
    Unavailable,
}

/// <summary>One GPU reading. Values are null when that sensor didn't answer, never a fake zero.</summary>
public readonly record struct GpuSample(GpuStatus Status, uint? Load, uint? TempC, ulong? VramUsed, ulong? VramTotal,
    uint? ClockMhz, double? PowerW, Limit Limits = Limit.None)
{
    public static GpuSample Of(GpuStatus status) => new(status, null, null, null, null, null, null);
}

/// <summary>
/// NVIDIA GPU telemetry through NVML (nvml.dll ships with the driver).
/// <para>
/// On Optimus laptops, asking NVML for sensors keeps the GPU powered. So before each read the device's
/// power state is checked through Windows' device manager (which doesn't touch the GPU); while it's in
/// D3 (asleep) no NVML call is made. If NVML starts failing (driver reset, GPU lost) it's closed and
/// re-opened with backoff instead of failing every second.
/// </para>
/// </summary>
internal sealed class NvidiaGpu : IDisposable
{
    [StructLayout(LayoutKind.Sequential)] private struct Utilization { public uint Gpu, Memory; }
    [StructLayout(LayoutKind.Sequential)] private struct Memory { public ulong Total, Free, Used; }

    private const string Dll = "nvml.dll";
    [DllImport(Dll)] private static extern int nvmlInit_v2();
    [DllImport(Dll)] private static extern int nvmlShutdown();
    [DllImport(Dll)] private static extern int nvmlDeviceGetHandleByIndex_v2(uint index, out IntPtr device);
    [DllImport(Dll)] private static extern int nvmlDeviceGetName(IntPtr device, byte[] name, uint length);
    [DllImport(Dll)] private static extern int nvmlDeviceGetTemperature(IntPtr device, int sensor, out uint temp);
    [DllImport(Dll)] private static extern int nvmlDeviceGetUtilizationRates(IntPtr device, out Utilization util);
    [DllImport(Dll)] private static extern int nvmlDeviceGetMemoryInfo(IntPtr device, out Memory mem);
    [DllImport(Dll)] private static extern int nvmlDeviceGetClockInfo(IntPtr device, int type, out uint mhz);
    [DllImport(Dll)] private static extern int nvmlDeviceGetPowerUsage(IntPtr device, out uint milliwatts);
    [DllImport(Dll)] private static extern int nvmlDeviceGetCurrentClocksThrottleReasons(IntPtr device, out ulong reasons);

    // nvmlClocksThrottleReason* bits
    private const ulong SwPowerCap = 0x4, HwSlowdown = 0x8, SwThermalSlowdown = 0x20, HwThermalSlowdown = 0x40, HwPowerBrake = 0x80;
    private const int NvmlErrorGpuIsLost = 15;

    private IntPtr _device;
    private bool _open;
    private int _failures;
    private long _retryAt;
    private readonly DevicePower? _power;

    public string Name { get; private set; } = "";
    public bool Present { get; }
    public bool CanDetectSleep => _power != null;
    public bool LetSleep { get; set; } = true;

    private NvidiaGpu(DevicePower? power)
    {
        _power = power;
        Present = Open();
    }

    /// <summary>Returns null on a PC without an NVIDIA driver.</summary>
    public static NvidiaGpu? TryCreate()
    {
        try
        {
            var gpu = new NvidiaGpu(DevicePower.Find("NVIDIA"));
            return gpu.Present ? gpu : null;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            Log.Info("GPU", "No NVIDIA driver (nvml.dll not found); GPU stats unavailable");
            return null;
        }
    }

    private bool Open()
    {
        if (nvmlInit_v2() != 0) return false;
        if (nvmlDeviceGetHandleByIndex_v2(0, out _device) != 0)
        {
            nvmlShutdown();
            return false;
        }
        var buf = new byte[96];
        Name = nvmlDeviceGetName(_device, buf, (uint)buf.Length) == 0 ? Encoding.ASCII.GetString(buf).TrimEnd('\0') : "NVIDIA GPU";
        _open = true;
        _failures = 0;
        return true;
    }

    public GpuSample Read()
    {
        if (LetSleep && _power?.IsAsleep() == true) return GpuSample.Of(GpuStatus.Sleeping);

        if (!_open)
        {
            if (Environment.TickCount64 < _retryAt) return GpuSample.Of(GpuStatus.Unavailable);
            try
            {
                if (!Open())
                {
                    _retryAt = Environment.TickCount64 + 10_000;
                    return GpuSample.Of(GpuStatus.Unavailable);
                }
                Log.Info("GPU", "Reconnected to the NVIDIA driver");
            }
            catch (Exception e)
            {
                _retryAt = Environment.TickCount64 + 30_000;
                Log.Error("GPU", "NVML re-open failed", e);
                return GpuSample.Of(GpuStatus.Unavailable);
            }
        }

        var loadRc = nvmlDeviceGetUtilizationRates(_device, out var u);
        uint? load = loadRc == 0 ? u.Gpu : null;
        uint? temp = nvmlDeviceGetTemperature(_device, 0, out var t) == 0 ? t : null;
        uint? clock = nvmlDeviceGetClockInfo(_device, 0, out var c) == 0 ? c : null;
        double? power = nvmlDeviceGetPowerUsage(_device, out var p) == 0 ? p / 1000.0 : null;
        var hasMem = nvmlDeviceGetMemoryInfo(_device, out var m) == 0;
        var limits = nvmlDeviceGetCurrentClocksThrottleReasons(_device, out var reasons) == 0 ? ToLimits(reasons) : Limit.None;

        if (load is null && temp is null && !hasMem)
        {
            // Nothing answered: count it, and after a few in a row (or a lost GPU) reconnect with backoff
            if (++_failures >= 3 || loadRc == NvmlErrorGpuIsLost)
            {
                Log.Warn("GPU", $"NVML stopped answering (error {loadRc}); reconnecting");
                Close();
                _retryAt = Environment.TickCount64 + 5_000;
            }
            return GpuSample.Of(GpuStatus.Unavailable);
        }
        _failures = 0;
        return new GpuSample(GpuStatus.Ok, load, temp, hasMem ? m.Used : null, hasMem ? m.Total : null, clock, power, limits);
    }

    private static Limit ToLimits(ulong reasons) =>
        ((reasons & (SwThermalSlowdown | HwThermalSlowdown | HwSlowdown)) != 0 ? Limit.GpuThermal : Limit.None) |
        ((reasons & (SwPowerCap | HwPowerBrake)) != 0 ? Limit.GpuPower : Limit.None);

    private void Close()
    {
        if (!_open) return;
        _open = false;
        try { nvmlShutdown(); } catch { /* driver gone */ }
    }

    public void Dispose() => Close();
}
