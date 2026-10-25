using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PCSpecOverlay.Core;

public sealed record ReleaseAsset(string Name, string Url, long Size);

/// <summary>A published release. <see cref="Sha256"/> comes from a "SHA256: …" line in the release notes.</summary>
public sealed record ReleaseInfo(Version Version, string Title, string Notes, string PageUrl, ReleaseAsset? Setup, string? Sha256)
{
    /// <summary>Only offered for one-click install when the setup file and its checksum are both published.</summary>
    public bool CanInstall => Setup != null && Sha256 != null;
}

public enum UpdateState { NotConfigured, UpToDate, Available, Failed }

public sealed record UpdateResult(UpdateState State, ReleaseInfo? Release, string Message);

/// <summary>
/// Checks GitHub Releases for a newer version and installs it safely:
/// <list type="bullet">
/// <item>Only api.github.com and GitHub's download host are contacted; the request carries the app version, nothing personal.</item>
/// <item>The setup file is only run after its size and SHA-256 match what the release publishes.</item>
/// <item>The installer still asks before replacing anything and keeps the previous version as a fallback.</item>
/// </list>
/// </summary>
public static class Updates
{
    /// <summary>"owner/name" of the GitHub repository, set at build time; empty until the project is published.</summary>
    public static string Repository => AppIdentity.UpdateRepository;

    public static async Task<UpdateResult> CheckAsync(HttpClient http, Version current, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(Repository))
            return new(UpdateState.NotConfigured, null, "Updates aren't set up yet: this build doesn't know where releases are published.");
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repository}/releases/latest");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            using var response = await http.SendAsync(request, ct);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return new(UpdateState.UpToDate, null, "No releases have been published yet.");
            response.EnsureSuccessStatusCode();
            var release = Parse(await response.Content.ReadAsStringAsync(ct));
            if (release is null) return new(UpdateState.Failed, null, "The latest release couldn't be read.");
            return release.Version > current
                ? new(UpdateState.Available, release, $"Version {release.Version} is available (you have {current}).")
                : new(UpdateState.UpToDate, release, $"You have the latest version ({current}).");
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            Log.Warn("Updates", $"Update check failed: {e.Message}");
            return new(UpdateState.Failed, null, $"Couldn't check for updates: {e.Message}");
        }
    }

    /// <summary>Reads a GitHub "latest release" JSON document. Null if it has no usable version.</summary>
    public static ReleaseInfo? Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
        if (!TryParseVersion(tag, out var version)) return null;

        var notes = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
        ReleaseAsset? setup = null;
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            foreach (var a in assets.EnumerateArray())
            {
                var name = a.GetProperty("name").GetString() ?? "";
                if (name.Contains("Setup", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    setup = new ReleaseAsset(name, a.GetProperty("browser_download_url").GetString() ?? "", a.GetProperty("size").GetInt64());
                    break;
                }
            }
        var hash = Regex.Match(notes, @"SHA-?256\s*[:=]\s*([0-9a-fA-F]{64})");
        return new ReleaseInfo(version,
            root.TryGetProperty("name", out var n) && n.GetString() is { Length: > 0 } title ? title : $"Version {version}",
            notes,
            root.TryGetProperty("html_url", out var u) ? u.GetString() ?? "" : "",
            setup,
            hash.Success ? hash.Groups[1].Value.ToUpperInvariant() : null);
    }

    public static bool TryParseVersion(string? tag, out Version version)
    {
        version = new Version(0, 0);
        var text = tag?.Trim().TrimStart('v', 'V').Split('-', '+')[0];
        if (!Version.TryParse(text, out var v)) return false;
        version = v;
        return true;
    }

    /// <summary>Where the setup file for a version is downloaded to (and deleted from if it fails the check).</summary>
    internal static string DownloadPath(Version version) =>
        Path.Combine(Path.GetTempPath(), "PCSpecOverlay-update", $"Optiframe-Setup-{version}.exe");

    /// <summary>Downloads the setup file and checks it. Returns the local path, or throws with a plain reason.</summary>
    public static async Task<string> DownloadAsync(HttpClient http, ReleaseInfo release, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (!release.CanInstall) throw new InvalidOperationException("This release doesn't publish a setup file with a checksum, so it can't be installed automatically.");
        var asset = release.Setup!;
        var path = DownloadPath(release.Version);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        using (var response = await http.GetAsync(asset.Url, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            response.EnsureSuccessStatusCode();
            await using var source = await response.Content.ReadAsStreamAsync(ct);
            await using var file = File.Create(path);
            var buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, ct)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read), ct);
                done += read;
                progress?.Report(asset.Size > 0 ? (double)done / asset.Size : 0);
            }
        }

        var actualSize = new FileInfo(path).Length;
        string actualHash;
        await using (var check = File.OpenRead(path))
            actualHash = Convert.ToHexString(await SHA256.HashDataAsync(check, ct));
        if (actualSize != asset.Size || actualHash != release.Sha256)
        {
            File.Delete(path);
            Log.Error("Updates", $"Downloaded setup didn't match the release (size {actualSize}/{asset.Size}, hash {actualHash}/{release.Sha256}); deleted");
            throw new InvalidOperationException("The downloaded file didn't match the published checksum, so it was deleted and nothing was installed.");
        }
        Log.Info("Updates", $"Downloaded and verified {asset.Name} ({actualSize} bytes, SHA-256 {actualHash})");
        return path;
    }

    public static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("PCSpecOverlay", AppIdentity.Version));
        return http;
    }
}
