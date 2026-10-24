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
