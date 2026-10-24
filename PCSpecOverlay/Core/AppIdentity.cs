using System.IO;
using System.Reflection;

namespace PCSpecOverlay.Core;

/// <summary>
/// Which copy of the app this is. The installed copy (under %LocalAppData%\Programs) is "production";
/// anything else — e.g. a build folder — is a development build and gets its own data folder, startup
/// task, single-instance lock and monitoring session, so it can't interfere with the installed app.
/// </summary>
public static class AppIdentity
{
    public const string ProductName = "PC Spec Overlay";
    public const string ExeName = "PCSpecOverlay.exe";

    public static string InstallDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "PCSpecOverlay");

    public static string InstalledExe => Path.Combine(InstallDirectory, ExeName);

    public static bool IsInstalledCopy => Environment.ProcessPath is { } p &&
        string.Equals(Path.GetFullPath(p), InstalledExe, StringComparison.OrdinalIgnoreCase);

    /// <summary>Running as the setup program (file named "...Setup.exe").</summary>
    public static bool IsSetup => Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? "")
        .Contains("Setup", StringComparison.OrdinalIgnoreCase);

    public static bool IsDevBuild => !IsInstalledCopy && !IsSetup;

    /// <summary>Appended to names that must not clash between the installed app and a dev build.</summary>
    public static string Suffix => IsDevBuild ? "-Dev" : "";

    /// <summary>GitHub "owner/name" that releases are published to; set at build time (UpdateRepository property).</summary>
    public static string UpdateRepository { get; } =
        typeof(AppIdentity).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "UpdateRepository")?.Value ?? "";

    public static string Version { get; } =
        typeof(AppIdentity).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? "0.0.0";

    /// <summary>A path with the user's profile folder replaced, for logs and diagnostic reports.</summary>
    public static string Redact(string? path)
    {
        if (string.IsNullOrEmpty(path)) return "(none)";
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return path.Replace(profile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase)
                   .Replace(Environment.UserName, "<user>", StringComparison.OrdinalIgnoreCase);
    }
}
