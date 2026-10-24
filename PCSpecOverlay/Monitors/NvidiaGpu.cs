using System.Runtime.InteropServices;
using System.Text;

namespace PCSpecOverlay.Monitors;

public readonly record struct GpuSample(uint? Load, uint? TempC, ulong? VramUsed, ulong? VramTotal, uint? ClockMhz, double? PowerW,
    Limit Limits = Limit.None);

/// <summary>Reads NVIDIA GPU telemetry through NVML (nvml.dll ships with the driver).</summary>
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

    private static Limit ToLimits(ulong reasons) =>
        ((reasons & (SwThermalSlowdown | HwThermalSlowdown | HwSlowdown)) != 0 ? Limit.GpuThermal : Limit.None) |
        ((reasons & (SwPowerCap | HwPowerBrake)) != 0 ? Limit.GpuPower : Limit.None);

    private readonly IntPtr _device;
    public string Name { get; }

    private NvidiaGpu(IntPtr device, string name)
    {
        _device = device;
        Name = name;
    }

    public static NvidiaGpu? TryOpen()
    {
        try
        {
            if (nvmlInit_v2() != 0) return null;
            if (nvmlDeviceGetHandleByIndex_v2(0, out var device) != 0)
            {
                nvmlShutdown();
                return null;
            }
            var buf = new byte[96];
            var name = nvmlDeviceGetName(device, buf, (uint)buf.Length) == 0
                ? Encoding.ASCII.GetString(buf).TrimEnd('\0')
                : "NVIDIA GPU";
            return new NvidiaGpu(device, name);
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    public GpuSample Read()
    {
        uint? load = nvmlDeviceGetUtilizationRates(_device, out var u) == 0 ? u.Gpu : null;
        uint? temp = nvmlDeviceGetTemperature(_device, 0, out var t) == 0 ? t : null;
        uint? clock = nvmlDeviceGetClockInfo(_device, 0, out var c) == 0 ? c : null;
        double? power = nvmlDeviceGetPowerUsage(_device, out var p) == 0 ? p / 1000.0 : null;
        var hasMem = nvmlDeviceGetMemoryInfo(_device, out var m) == 0;
        var limits = nvmlDeviceGetCurrentClocksThrottleReasons(_device, out var reasons) == 0 ? ToLimits(reasons) : Limit.None;
        return new GpuSample(load, temp, hasMem ? m.Used : null, hasMem ? m.Total : null, clock, power, limits);
    }

    public void Dispose() => nvmlShutdown();
}
