using System.Management;
using System.Runtime.InteropServices;
using PCSpecOverlay.Core;

namespace PCSpecOverlay.Monitors;

/// <summary>
/// Reads a device's current power state (D0 = on … D3 = off) from Windows' device manager. This is
/// bookkeeping the OS already has, so asking doesn't wake the device — unlike asking its driver.
/// </summary>
internal sealed class DevicePower
{
    [StructLayout(LayoutKind.Sequential)] private struct DEVPROPKEY { public Guid fmtid; public uint pid; }

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Locate_DevNodeW(out uint devInst, string id, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_DevNode_PropertyW(uint devInst, ref DEVPROPKEY key, out uint type, byte[] buffer, ref uint size, uint flags);

    // DEVPKEY_Device_PowerData → CM_POWER_DATA; PD_MostRecentPowerState is the int at offset 4
    private static DEVPROPKEY PowerData = new() { fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), pid = 32 };
    private const int PowerDeviceD3 = 4;

    private readonly string _instanceId;
    private readonly byte[] _buffer = new byte[64];

    private DevicePower(string instanceId) => _instanceId = instanceId;

    /// <summary>The first display adapter whose name contains <paramref name="vendor"/>, or null.</summary>
    public static DevicePower? Find(string vendor)
    {
        try
        {
            using var search = new ManagementObjectSearcher("SELECT Name, PNPDeviceID FROM Win32_VideoController");
            foreach (var o in search.Get())
                using (o)
                    if (o["Name"] is string name && name.Contains(vendor, StringComparison.OrdinalIgnoreCase) && o["PNPDeviceID"] is string id)
                        return new DevicePower(id);
        }
        catch (Exception e) { Log.Warn("GPU", $"Couldn't look up the GPU device for sleep detection: {e.Message}"); }
        return null;
    }

    /// <summary>True only when Windows reports the device in D3. Unknown counts as awake.</summary>
    public bool IsAsleep()
    {
        if (CM_Locate_DevNodeW(out var dev, _instanceId, 0) != 0) return false;
        uint size = (uint)_buffer.Length;
        if (CM_Get_DevNode_PropertyW(dev, ref PowerData, out _, _buffer, ref size, 0) != 0 || size < 8) return false;
        return BitConverter.ToInt32(_buffer, 4) == PowerDeviceD3;
    }
}
