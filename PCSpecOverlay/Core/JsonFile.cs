using System.IO;
using System.Text.Json;

namespace PCSpecOverlay.Core;

/// <summary>
/// Atomic JSON persistence: writes go to a temp file that then replaces the original, so a crash
/// mid-write never leaves a half-written file. A file that fails to parse is kept aside (not
/// overwritten) so nothing the user had is silently lost.
/// </summary>
public static class JsonFile
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static T Load<T>(string path, string area) where T : new()
    {
        if (!File.Exists(path)) return new T();
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options) ?? new T();
        }
        catch (Exception e)
        {
            var aside = $"{path}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}";
            try { File.Move(path, aside); } catch { /* leave it in place */ }
            Log.Error(area, $"Couldn't read {Path.GetFileName(path)}; using defaults and keeping the old file as {Path.GetFileName(aside)}", e);
            return new T();
        }
    }

    public static bool Save<T>(string path, T value, string area)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(value, Options));
            File.Move(temp, path, overwrite: true);
            return true;
        }
        catch (Exception e)
        {
            Log.Error(area, $"Couldn't save {Path.GetFileName(path)}", e);
            return false;
        }
    }
}
