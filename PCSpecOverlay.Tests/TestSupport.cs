using PCSpecOverlay.Core;

// Tests share AppPaths (static), so they must not run in parallel
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace PCSpecOverlay.Tests;

/// <summary>Points the app's data folder at a fresh temp directory for each test.</summary>
public abstract class TempDataTest : IDisposable
{
    protected readonly string Dir = Path.Combine(Path.GetTempPath(), "PCSpecOverlayTests", Guid.NewGuid().ToString("N"));

    protected TempDataTest()
    {
        Directory.CreateDirectory(Dir);
        AppPaths.DataDirectory = Dir;
    }

    public virtual void Dispose()
    {
        try { Directory.Delete(Dir, recursive: true); } catch { /* best effort */ }
    }
}

/// <summary>An in-memory setting with knobs for failure cases.</summary>
public sealed class FakeSetting(string id = "fake", string? initial = "original") : IReversibleSetting
{
    public string? Value = initial;
    public string Id => id;
    public string Name => "Fake setting";
    public string? Unsupported { get; set; }
    public bool IgnoreWrites { get; set; }
    public bool ThrowOnWrite { get; set; }
    public int Writes { get; private set; }

    public string? Read() => Value;

    public void Write(string? value)
    {
        Writes++;
        if (ThrowOnWrite) throw new InvalidOperationException("write failed");
        if (!IgnoreWrites) Value = value;
    }
}

/// <summary>Runs WPF code on a fresh STA thread and rethrows anything it throws.</summary>
public static class Sta
{
    public static void Run(Action action)
    {
        Exception? error = null;
        var t = new Thread(() => { try { action(); } catch (Exception e) { error = e; } });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
    }
}

/// <summary>Finds files in the source tree (tests run from bin, so walk up from this file's location).</summary>
public static class SourceFiles
{
    public static string Path(string relative, [System.Runtime.CompilerServices.CallerFilePath] string here = "") =>
        System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(here)!, "..", relative));
}

public static class XamlFile
{
    public static System.Windows.ResourceDictionary LoadDictionary(string relative)
    {
        using var stream = File.OpenRead(SourceFiles.Path(relative));
        return (System.Windows.ResourceDictionary)System.Windows.Markup.XamlReader.Load(stream);
    }
}

/// <summary>WCAG 2 contrast ratio between two colors (1 to 21).</summary>
public static class Contrast
{
    public static double Ratio(System.Windows.Media.Color a, System.Windows.Media.Color b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double Luminance(System.Windows.Media.Color c) => 0.2126 * Lin(c.R) + 0.7152 * Lin(c.G) + 0.0722 * Lin(c.B);

    private static double Lin(byte v)
    {
        var c = v / 255.0;
        return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }
}
