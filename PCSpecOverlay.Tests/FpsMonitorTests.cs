using System.Diagnostics;
using System.Runtime.InteropServices;
using PCSpecOverlay.Monitors;
using Forms = System.Windows.Forms;

namespace PCSpecOverlay.Tests;

/// <summary>
/// Renders real frames with OpenGL and checks the FPS monitor counts them. Needs the same permission as
/// the app (administrator or Performance Log Users); without it the test reports why and passes vacuously.
/// </summary>
public class FpsMonitorTests : TempDataTest
{
    [Fact]
    public void Counts_opengl_frames_through_the_graphics_kernel()
    {
        using var fps = new FpsMonitor();
        if (!fps.Start())
        {
            Console.WriteLine($"Skipped: frame monitoring isn't permitted for this account ({fps.LastError?.Message})");
            return;
        }
        Assert.True(fps.KernelPresents, "the graphics kernel provider should be enabled");

        using var gl = GlWindow.Start(targetFps: 60);
        Thread.Sleep(4000); // let a few seconds of frames arrive

        var sample = fps.Sample(Environment.ProcessId, gl.Handle);
        Assert.NotNull(sample);
        Assert.Equal(FpsMonitor.Source.Kernel, fps.LastSource);
        Assert.InRange(sample!.Value.Fps, 55, 65);
        Assert.InRange(sample.Value.FrameTimeMs, 15, 18.5);
    }

    /// <summary>
    /// Vulkan on an integrated GPU presents through the graphics kernel; on NVIDIA laptops the driver hands
    /// frames to DXGI. Different target rates per case make a wrong match impossible to miss.
    /// </summary>
    [Theory]
    [InlineData("Intel", 48)]
    [InlineData("NVIDIA", 72)]
    public void Counts_vulkan_frames(string gpu, int targetFps)
    {
        using var fps = new FpsMonitor();
        if (!fps.Start())
        {
            Console.WriteLine($"Skipped: frame monitoring isn't permitted for this account ({fps.LastError?.Message})");
            return;
        }

        using var vulkan = VulkanWindow.TryStart(targetFps, gpu, out var why);
        if (vulkan is null)
        {
            Console.WriteLine($"Skipped: no Vulkan on a {gpu} GPU here ({why})");
            return;
        }
        Thread.Sleep(4000);

        var sample = fps.Sample(Environment.ProcessId, vulkan.Handle);
        Assert.NotNull(sample);
        Assert.InRange(sample!.Value.Fps, targetFps * 0.92, targetFps * 1.08);
        Console.WriteLine($"Vulkan on {vulkan.GpuName}: {sample.Value.Fps:0.0} FPS via {fps.LastSource}");
    }

    [Fact]
    public void Stopped_rendering_reads_as_unavailable_not_zero()
    {
        using var fps = new FpsMonitor();
        if (!fps.Start()) return;
        IntPtr window;
        using (var gl = GlWindow.Start(targetFps: 60))
        {
            Thread.Sleep(3000);
            window = gl.Handle;
            Assert.NotNull(fps.Sample(Environment.ProcessId, window));
        }
        Assert.True(fps.IsPresenting(Environment.ProcessId, (ulong)window));
        // Past the 2 s staleness limit, plus the ~1 s Windows takes to deliver buffered events.
        // Checked for this window specifically: other test windows share this test process.
        Thread.Sleep(4000);
        Assert.False(fps.IsPresenting(Environment.ProcessId, (ulong)window));
    }

    /// <summary>A real window that clears and SwapBuffers with OpenGL at a fixed rate, on its own UI thread.</summary>
    private sealed class GlWindow : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct PIXELFORMATDESCRIPTOR
        {
            public ushort nSize, nVersion; public uint dwFlags; public byte iPixelType, cColorBits, cRedBits, cRedShift, cGreenBits, cGreenShift,
            cBlueBits, cBlueShift, cAlphaBits, cAlphaShift, cAccumBits, cAccumRedBits, cAccumGreenBits, cAccumBlueBits, cAccumAlphaBits,
            cDepthBits, cStencilBits, cAuxBuffers, iLayerType, bReserved; public uint dwLayerMask, dwVisibleMask, dwDamageMask;
        }
        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
        [DllImport("gdi32.dll")] private static extern int ChoosePixelFormat(IntPtr hdc, ref PIXELFORMATDESCRIPTOR pfd);
        [DllImport("gdi32.dll")] private static extern bool SetPixelFormat(IntPtr hdc, int format, ref PIXELFORMATDESCRIPTOR pfd);
        [DllImport("gdi32.dll")] private static extern bool SwapBuffers(IntPtr hdc);
        [DllImport("opengl32.dll")] private static extern IntPtr wglCreateContext(IntPtr hdc);
        [DllImport("opengl32.dll")] private static extern bool wglMakeCurrent(IntPtr hdc, IntPtr ctx);
        [DllImport("opengl32.dll")] private static extern bool wglDeleteContext(IntPtr ctx);
        [DllImport("opengl32.dll")] private static extern IntPtr wglGetProcAddress(string name);
        [DllImport("opengl32.dll")] private static extern void glClearColor(float r, float g, float b, float a);
        [DllImport("opengl32.dll")] private static extern void glClear(uint mask);
        private delegate bool SwapInterval(int interval);

        private readonly Thread _thread;
        private readonly ManualResetEventSlim _ready = new();
        private volatile bool _stop;
        public IntPtr Handle { get; private set; }

        private GlWindow(int targetFps)
        {
            _thread = new Thread(() => Run(targetFps)) { IsBackground = true };
            _thread.SetApartmentState(ApartmentState.STA);
        }

        public static GlWindow Start(int targetFps)
        {
            var w = new GlWindow(targetFps);
            w._thread.Start();
            Assert.True(w._ready.Wait(10_000), "OpenGL window didn't start");
            return w;
        }

        private void Run(int targetFps)
        {
            using var form = new Forms.Form { Text = "FPS test", Width = 320, Height = 240, ShowInTaskbar = false };
            form.Show();
            var hdc = GetDC(form.Handle);
            var pfd = new PIXELFORMATDESCRIPTOR { nSize = 40, nVersion = 1, dwFlags = 0x4 | 0x20 | 0x1, cColorBits = 32, cDepthBits = 24 };
            SetPixelFormat(hdc, ChoosePixelFormat(hdc, ref pfd), ref pfd);
            var ctx = wglCreateContext(hdc);
            wglMakeCurrent(hdc, ctx);
            var swapInterval = wglGetProcAddress("wglSwapIntervalEXT");
            if (swapInterval != IntPtr.Zero) Marshal.GetDelegateForFunctionPointer<SwapInterval>(swapInterval)(0);
            Handle = form.Handle;
            _ready.Set();

            var clock = Stopwatch.StartNew();
            double next = 0;
            while (!_stop)
            {
                Forms.Application.DoEvents();
                glClearColor(0.2f, 0.6f, 0.2f, 1);
                glClear(0x4000);
                SwapBuffers(hdc);
                next += 1000.0 / targetFps;
                var wait = next - clock.Elapsed.TotalMilliseconds;
                if (wait > 1) Thread.Sleep((int)wait);
            }
            wglMakeCurrent(IntPtr.Zero, IntPtr.Zero);
            wglDeleteContext(ctx);
            form.Close();
        }

        public void Dispose()
        {
            _stop = true;
            _thread.Join(5000);
        }
    }
}
