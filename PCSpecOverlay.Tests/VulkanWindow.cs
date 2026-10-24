using System.Diagnostics;
using System.Runtime.InteropServices;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using Forms = System.Windows.Forms;

namespace PCSpecOverlay.Tests;

/// <summary>
/// A real window that clears and presents with Vulkan at a fixed rate, on a chosen GPU, on its own
/// UI thread. <see cref="TryStart"/> returns null (with a reason) when no such GPU can present.
/// </summary>
internal sealed unsafe class VulkanWindow : IDisposable
{
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new();
    private volatile bool _stop;
    private string? _error;

    public IntPtr Handle { get; private set; }
    public string GpuName { get; private set; } = "";

    private VulkanWindow(int fps, string gpuFilter)
    {
        _thread = new Thread(() =>
        {
            try { Run(fps, gpuFilter); }
            catch (Exception e) { _error = e.Message; _ready.Set(); }
        }) { IsBackground = true };
        _thread.SetApartmentState(ApartmentState.STA);
    }

    public static VulkanWindow? TryStart(int fps, string gpuFilter, out string? why)
    {
        var w = new VulkanWindow(fps, gpuFilter);
        w._thread.Start();
        if (!w._ready.Wait(15_000)) { why = "timed out"; w.Dispose(); return null; }
        why = w._error;
        if (why is null) return w;
        w.Dispose();
        return null;
    }

    private void Run(int fps, string gpuFilter)
    {
        using var form = new Forms.Form { Text = "Vulkan FPS test", Width = 320, Height = 240, ShowInTaskbar = false };
        form.Show();
        Forms.Application.DoEvents();

        var vk = Vk.GetApi();
        var exts = (byte**)SilkMarshal.StringArrayToPtr(new[] { "VK_KHR_surface", "VK_KHR_win32_surface" });
        var app = new ApplicationInfo { SType = StructureType.ApplicationInfo, ApiVersion = Vk.Version11 };
        var ici = new InstanceCreateInfo { SType = StructureType.InstanceCreateInfo, PApplicationInfo = &app, EnabledExtensionCount = 2, PpEnabledExtensionNames = exts };
        Check(vk.CreateInstance(in ici, null, out var instance), "instance");
        try
        {
            vk.TryGetInstanceExtension(instance, out KhrWin32Surface win32);
            vk.TryGetInstanceExtension(instance, out KhrSurface khrSurface);
            var sci = new Win32SurfaceCreateInfoKHR { SType = StructureType.Win32SurfaceCreateInfoKhr, Hinstance = Marshal.GetHINSTANCE(typeof(VulkanWindow).Module), Hwnd = form.Handle };
            Check(win32.CreateWin32Surface(instance, in sci, null, out var surface), "surface");

            uint count = 0;
            vk.EnumeratePhysicalDevices(instance, ref count, null);
            var devices = new PhysicalDevice[count];
            fixed (PhysicalDevice* p = devices) vk.EnumeratePhysicalDevices(instance, ref count, p);
            PhysicalDevice gpu = default;
            var family = uint.MaxValue;
            foreach (var d in devices)
            {
                vk.GetPhysicalDeviceProperties(d, out var props);
                var name = SilkMarshal.PtrToString((nint)props.DeviceName) ?? "";
                if (!name.Contains(gpuFilter, StringComparison.OrdinalIgnoreCase)) continue;
                uint qc = 0;
                vk.GetPhysicalDeviceQueueFamilyProperties(d, ref qc, null);
                var qs = new QueueFamilyProperties[qc];
                fixed (QueueFamilyProperties* q = qs) vk.GetPhysicalDeviceQueueFamilyProperties(d, ref qc, q);
                for (uint i = 0; i < qc && family == uint.MaxValue; i++)
                {
                    khrSurface.GetPhysicalDeviceSurfaceSupport(d, i, surface, out var present);
                    if ((qs[i].QueueFlags & QueueFlags.GraphicsBit) != 0 && present) { gpu = d; family = i; GpuName = name; }
                }
                if (family != uint.MaxValue) break;
            }
            if (family == uint.MaxValue) throw new InvalidOperationException($"no '{gpuFilter}' GPU can present to a window");

            var prio = 1f;
            var qci = new DeviceQueueCreateInfo { SType = StructureType.DeviceQueueCreateInfo, QueueFamilyIndex = family, QueueCount = 1, PQueuePriorities = &prio };
            var devExt = (byte**)SilkMarshal.StringArrayToPtr(new[] { "VK_KHR_swapchain" });
            var dci = new DeviceCreateInfo { SType = StructureType.DeviceCreateInfo, QueueCreateInfoCount = 1, PQueueCreateInfos = &qci, EnabledExtensionCount = 1, PpEnabledExtensionNames = devExt };
            Check(vk.CreateDevice(gpu, in dci, null, out var device), "device");
            vk.GetDeviceQueue(device, family, 0, out var queue);
            vk.TryGetDeviceExtension(instance, device, out KhrSwapchain khrSwap);

            khrSurface.GetPhysicalDeviceSurfaceCapabilities(gpu, surface, out var caps);
            uint fc = 0;
            khrSurface.GetPhysicalDeviceSurfaceFormats(gpu, surface, ref fc, null);
            var formats = new SurfaceFormatKHR[fc];
            fixed (SurfaceFormatKHR* f = formats) khrSurface.GetPhysicalDeviceSurfaceFormats(gpu, surface, ref fc, f);
            var swci = new SwapchainCreateInfoKHR
            {
                SType = StructureType.SwapchainCreateInfoKhr, Surface = surface, MinImageCount = caps.MinImageCount + 1,
                ImageFormat = formats[0].Format, ImageColorSpace = formats[0].ColorSpace, ImageExtent = caps.CurrentExtent,
                ImageArrayLayers = 1, ImageUsage = ImageUsageFlags.TransferDstBit | ImageUsageFlags.ColorAttachmentBit,
                ImageSharingMode = SharingMode.Exclusive, PreTransform = caps.CurrentTransform,
                CompositeAlpha = CompositeAlphaFlagsKHR.OpaqueBitKhr, PresentMode = PresentModeKHR.FifoKhr, Clipped = true,
            };
            Check(khrSwap.CreateSwapchain(device, in swci, null, out var swapchain), "swapchain");
            uint ic = 0;
            khrSwap.GetSwapchainImages(device, swapchain, &ic, null);
            var images = new Silk.NET.Vulkan.Image[ic];
            fixed (Silk.NET.Vulkan.Image* im = images) khrSwap.GetSwapchainImages(device, swapchain, &ic, im);

            var pci = new CommandPoolCreateInfo { SType = StructureType.CommandPoolCreateInfo, QueueFamilyIndex = family };
            Check(vk.CreateCommandPool(device, in pci, null, out var pool), "command pool");
            var cbs = new CommandBuffer[ic];
            var cai = new CommandBufferAllocateInfo { SType = StructureType.CommandBufferAllocateInfo, CommandPool = pool, Level = CommandBufferLevel.Primary, CommandBufferCount = ic };
            fixed (CommandBuffer* c = cbs) vk.AllocateCommandBuffers(device, in cai, c);
            var range = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1);
            for (var i = 0; i < ic; i++)
            {
                var bi = new CommandBufferBeginInfo { SType = StructureType.CommandBufferBeginInfo };
                vk.BeginCommandBuffer(cbs[i], in bi);
                var toDst = new ImageMemoryBarrier
                {
                    SType = StructureType.ImageMemoryBarrier, OldLayout = Silk.NET.Vulkan.ImageLayout.Undefined, NewLayout = Silk.NET.Vulkan.ImageLayout.TransferDstOptimal,
                    SrcQueueFamilyIndex = Vk.QueueFamilyIgnored, DstQueueFamilyIndex = Vk.QueueFamilyIgnored, Image = images[i], SubresourceRange = range, DstAccessMask = AccessFlags.TransferWriteBit,
                };
                vk.CmdPipelineBarrier(cbs[i], PipelineStageFlags.TopOfPipeBit, PipelineStageFlags.TransferBit, 0, 0, null, 0, null, 1, &toDst);
                var color = new ClearColorValue(0.2f, 0.4f, 0.9f, 1f);
                vk.CmdClearColorImage(cbs[i], images[i], Silk.NET.Vulkan.ImageLayout.TransferDstOptimal, &color, 1, &range);
                var toPresent = new ImageMemoryBarrier
                {
                    SType = StructureType.ImageMemoryBarrier, OldLayout = Silk.NET.Vulkan.ImageLayout.TransferDstOptimal, NewLayout = Silk.NET.Vulkan.ImageLayout.PresentSrcKhr,
                    SrcQueueFamilyIndex = Vk.QueueFamilyIgnored, DstQueueFamilyIndex = Vk.QueueFamilyIgnored, Image = images[i], SubresourceRange = range, SrcAccessMask = AccessFlags.TransferWriteBit,
                };
                vk.CmdPipelineBarrier(cbs[i], PipelineStageFlags.TransferBit, PipelineStageFlags.BottomOfPipeBit, 0, 0, null, 0, null, 1, &toPresent);
                vk.EndCommandBuffer(cbs[i]);
            }

            var semInfo = new SemaphoreCreateInfo { SType = StructureType.SemaphoreCreateInfo };
            vk.CreateSemaphore(device, in semInfo, null, out var acquired);
            vk.CreateSemaphore(device, in semInfo, null, out var rendered);
            var fenceInfo = new FenceCreateInfo { SType = StructureType.FenceCreateInfo };
            vk.CreateFence(device, in fenceInfo, null, out var fence);

            Handle = form.Handle;
            _ready.Set();

            var clock = Stopwatch.StartNew();
            double next = 0;
            while (!_stop)
            {
                Forms.Application.DoEvents();
                uint index = 0;
                khrSwap.AcquireNextImage(device, swapchain, ulong.MaxValue, acquired, default, ref index);
                var waitStage = PipelineStageFlags.TransferBit;
                var cb = cbs[index];
                var submit = new SubmitInfo
                {
                    SType = StructureType.SubmitInfo, WaitSemaphoreCount = 1, PWaitSemaphores = &acquired, PWaitDstStageMask = &waitStage,
                    CommandBufferCount = 1, PCommandBuffers = &cb, SignalSemaphoreCount = 1, PSignalSemaphores = &rendered,
                };
                vk.QueueSubmit(queue, 1, in submit, fence);
                var sc = swapchain;
                var pi = new PresentInfoKHR { SType = StructureType.PresentInfoKhr, WaitSemaphoreCount = 1, PWaitSemaphores = &rendered, SwapchainCount = 1, PSwapchains = &sc, PImageIndices = &index };
                khrSwap.QueuePresent(queue, in pi);
                vk.WaitForFences(device, 1, in fence, true, ulong.MaxValue);
                vk.ResetFences(device, 1, in fence);
                next += 1000.0 / fps;
                var wait = next - clock.Elapsed.TotalMilliseconds;
                if (wait > 1) Thread.Sleep((int)wait);
            }

            vk.DeviceWaitIdle(device);
            vk.DestroyFence(device, fence, null);
            vk.DestroySemaphore(device, rendered, null);
            vk.DestroySemaphore(device, acquired, null);
            vk.DestroyCommandPool(device, pool, null);
            khrSwap.DestroySwapchain(device, swapchain, null);
            vk.DestroyDevice(device, null);
            khrSurface.DestroySurface(instance, surface, null);
        }
        finally
        {
            vk.DestroyInstance(instance, null);
        }
    }

    private static void Check(Result r, string what)
    {
        if (r != Result.Success) throw new InvalidOperationException($"Vulkan {what}: {r}");
    }

    public void Dispose()
    {
        _stop = true;
        _thread.Join(5000);
    }
}
