using System.Net.Http;
using PCSpecOverlay.Core;

namespace PCSpecOverlay.Tests;

public class UpdateTests
{
    private const string Hash = "3F5B1E0C620E5B16DA633E1FB50C27AAA97151EBD09D730B1FC6D44AE85C9E3C";

    private static string Release(string tag, string body, params (string Name, long Size)[] assets) =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            tag_name = tag,
            name = $"Optiframe {tag}",
            body,
            html_url = $"https://github.com/me/pcspec/releases/tag/{tag}",
            assets = assets.Select(a => new { name = a.Name, size = a.Size, browser_download_url = $"https://github.com/me/pcspec/releases/download/{tag}/{a.Name}" }),
        });

    [Fact]
    public void Reads_version_setup_file_and_checksum()
    {
        var r = Updates.Parse(Release("v2.1.0", $"Fixes.\n\nSHA256: {Hash.ToLowerInvariant()}", ("PCSpecOverlay-Setup-2.1.0.exe", 9_400_000), ("source.zip", 10)))!;
        Assert.Equal(new Version(2, 1, 0), r.Version);
        Assert.Equal("PCSpecOverlay-Setup-2.1.0.exe", r.Setup!.Name);
        Assert.Equal(9_400_000, r.Setup.Size);
        Assert.Equal(Hash, r.Sha256);
        Assert.True(r.CanInstall);
    }

    [Fact]
    public void Release_without_a_checksum_cant_be_installed_automatically()
    {
        var r = Updates.Parse(Release("v2.1.0", "No hash here.", ("PCSpecOverlay-Setup-2.1.0.exe", 1)))!;
        Assert.Null(r.Sha256);
        Assert.False(r.CanInstall);
    }

    [Fact]
    public void Release_without_a_setup_file_cant_be_installed_automatically() =>
        Assert.False(Updates.Parse(Release("v2.1.0", $"SHA256: {Hash}", ("notes.txt", 1)))!.CanInstall);

    [Theory]
    [InlineData("v2.0.10", 2, 0, 10)]
    [InlineData("2.1", 2, 1, -1)]
    [InlineData("V3.0.0-beta", 3, 0, 0)]
    public void Reads_common_tag_styles(string tag, int major, int minor, int build)
    {
        Assert.True(Updates.TryParseVersion(tag, out var v));
        Assert.Equal(new Version(major, minor, build == -1 ? 0 : build).ToString(build == -1 ? 2 : 3), v.ToString(build == -1 ? 2 : 3));
    }

    [Fact]
    public void Ten_is_newer_than_nine_not_older()
    {
        Updates.TryParseVersion("v2.0.10", out var ten);
        Updates.TryParseVersion("v2.0.9", out var nine);
        Assert.True(ten > nine); // a string comparison would get this wrong
    }

    [Fact]
    public void Unversioned_release_is_ignored() =>
        Assert.Null(Updates.Parse(Release("latest-build", "", ("PCSpecOverlay-Setup.exe", 1))));

    [Fact]
    public async Task Not_configured_is_reported_without_contacting_anything()
    {
        if (!string.IsNullOrEmpty(Updates.Repository)) return; // a build with a repository set checks for real
        using var http = new HttpClient(new FailingHandler());
        var r = await Updates.CheckAsync(http, new Version(2, 0, 10));
        Assert.Equal(UpdateState.NotConfigured, r.State);
    }

    [Fact]
    public async Task Tampered_download_is_deleted_and_never_run()
    {
        var release = Updates.Parse(Release("v9.9.9", $"SHA256: {Hash}", ("PCSpecOverlay-Setup-9.9.9.exe", 5)))!;
        using var http = new HttpClient(new FixedHandler([1, 2, 3, 4, 5])); // right size, wrong content
        var e = await Assert.ThrowsAsync<InvalidOperationException>(() => Updates.DownloadAsync(http, release));
        Assert.Contains("didn't match", e.Message);
        Assert.False(File.Exists(Updates.DownloadPath(release.Version)));
    }

    [Fact]
    public async Task Matching_download_is_kept()
    {
        byte[] content = [10, 20, 30];
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(content));
        var release = Updates.Parse(Release("v9.9.8", $"SHA256: {hash}", ("PCSpecOverlay-Setup-9.9.8.exe", content.Length)))!;
        using var http = new HttpClient(new FixedHandler(content));
        var path = await Updates.DownloadAsync(http, release);
        Assert.Equal(Updates.DownloadPath(release.Version), path);
        Assert.Equal(content, File.ReadAllBytes(path));
        File.Delete(path);
    }

    private sealed class FixedHandler(byte[] content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(content) });
    }

    private sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw new InvalidOperationException("no network call expected");
    }
}

public class ReleaseNotesTests
{
    [Fact]
    public void The_current_version_has_whats_new_without_a_checksum()
    {
        var csproj = File.ReadAllText(SourceFiles.Path("PCSpecOverlay/PCSpecOverlay.csproj"));
        var version = System.Text.RegularExpressions.Regex.Match(csproj, "<Version>([^<]+)</Version>").Groups[1].Value;
        Assert.Equal("2.1.1", version);
        var notes = File.ReadAllText(SourceFiles.Path($"docs/releases/{version}.md"));
        foreach (var topic in new[] { "Strip", "spacing" })
            Assert.Contains(topic, notes, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch(@"SHA-?256\s*[:=]", notes); // the script adds the one checksum line the updater reads
    }

    [Fact]
    public void Version_2_1_0_has_whats_new_and_the_script_includes_it()
    {
        var notes = File.ReadAllText(SourceFiles.Path("docs/releases/2.1.0.md"));
        foreach (var topic in new[] { "Optiframe", "light", "Strip", "pin", "Undo", "30", "100 °C", "Start menu", "settings" })
            Assert.Contains(topic, notes, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch(@"SHA-?256\s*[:=]", notes); // the script adds the one checksum line the updater reads

        var script = File.ReadAllText(SourceFiles.Path("build-release.ps1"));
        Assert.Contains(@"docs\releases\$version.md", script);
        Assert.Contains("SHA256: $hash", script);
        Assert.Contains("-Raw -Encoding UTF8", script); // the notes are UTF-8 without a BOM; 5.1 would read them as ANSI
    }
}
