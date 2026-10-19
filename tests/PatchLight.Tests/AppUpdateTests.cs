using System.Net;
using System.Security.Cryptography;
using PatchLight.Core;
using PatchLight.Windows;

namespace PatchLight.Tests;

public sealed class AppUpdateTests : IDisposable
{
    private const string Prefix = AppUpdater.DownloadPrefix;
    private static readonly byte[] Installer = "pretend installer"u8.ToArray();
    private static readonly string InstallerHash = Convert.ToHexStringLower(SHA256.HashData(Installer));
    private readonly string dir = Path.Combine(Path.GetTempPath(), "patchlight-update-" + Guid.NewGuid());

    public void Dispose()
    {
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }

    // Trimmed copy of GitHub's answer for a real release.
    private static string Latest(string tag = "v0.5.2", string? digest = null, string? url = null, bool draft = false) => $$"""
        {
          "html_url": "https://github.com/G4SBO/PatchLight/releases/tag/{{tag}}",
          "tag_name": "{{tag}}",
          "draft": {{(draft ? "true" : "false")}},
          "prerelease": false,
          "assets": [
            { "name": "PatchLight-win-x64.zip", "digest": "sha256:{{new string('a', 64)}}",
              "browser_download_url": "{{Prefix}}{{tag}}/PatchLight-win-x64.zip" },
            { "name": "PatchLight-Setup.exe", "digest": "{{digest ?? "sha256:" + InstallerHash}}",
              "browser_download_url": "{{url ?? Prefix + tag + "/PatchLight-Setup.exe"}}" }
          ]
        }
        """;

    [Fact]
    public void Reads_the_installer_and_its_hash()
    {
        var release = AppReleaseParser.Parse(Latest(), Prefix);

        Assert.NotNull(release);
        Assert.Equal(new Version(0, 5, 2), release.Version);
        Assert.Equal(Prefix + "v0.5.2/PatchLight-Setup.exe", release.SetupUrl);
        Assert.Equal(InstallerHash, release.Sha256);
        Assert.Equal("https://github.com/G4SBO/PatchLight/releases/tag/v0.5.2", release.PageUrl);
    }

    [Theory]
    [InlineData("https://evil.example/PatchLight-Setup.exe", null)]
    [InlineData(null, "")]
    [InlineData(null, "md5:abc")]
    public void Refuses_installers_from_elsewhere_or_without_a_hash(string? url, string? digest)
    {
        Assert.Null(AppReleaseParser.Parse(Latest(url: url, digest: digest), Prefix));
    }

    [Fact]
    public void Ignores_drafts_odd_tags_and_broken_answers()
    {
        Assert.Null(AppReleaseParser.Parse(Latest(draft: true), Prefix));
        Assert.Null(AppReleaseParser.Parse(Latest(tag: "nightly"), Prefix));
        Assert.Null(AppReleaseParser.Parse("{ not json", Prefix));
        Assert.Null(AppReleaseParser.Parse("""{ "message": "API rate limit exceeded" }""", Prefix));
    }

    [Theory]
    [InlineData("0.5.1.0", true)]
    [InlineData("0.5.2.0", false)]
    [InlineData("0.6.0.0", false)]
    [InlineData("0.4.9.0", true)]
    public void Only_a_higher_version_counts_as_newer(string current, bool newer)
    {
        var release = AppReleaseParser.Parse(Latest("v0.5.2"), Prefix)!;
        Assert.Equal(newer, AppReleaseParser.IsNewer(release, Version.Parse(current)));
    }

    [Fact]
    public async Task Check_finds_a_newer_release()
    {
        var updater = Updater(_ => Ok(Latest("v0.5.2")), "0.5.1.0");

        var check = await updater.CheckAsync(CancellationToken.None);

        Assert.True(check.Reached);
        Assert.Equal(new Version(0, 5, 2), check.Newer?.Version);
    }

    [Fact]
    public async Task Check_says_up_to_date_or_unreachable()
    {
        var current = await Updater(_ => Ok(Latest("v0.5.2")), "0.5.2.0").CheckAsync(CancellationToken.None);
        Assert.True(current.Reached);
        Assert.Null(current.Newer);

        var offline = await Updater(_ => throw new HttpRequestException("offline"), "0.5.1.0").CheckAsync(CancellationToken.None);
        Assert.False(offline.Reached);

        var limited = await Updater(_ => new HttpResponseMessage(HttpStatusCode.Forbidden), "0.5.1.0").CheckAsync(CancellationToken.None);
        Assert.False(limited.Reached);
    }

    [Fact]
    public async Task Download_keeps_a_matching_installer_locked()
    {
        var release = AppReleaseParser.Parse(Latest(), Prefix)!;
        var updater = Updater(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Installer) }, "0.5.1.0");

        await using var file = await updater.DownloadAsync(release, CancellationToken.None);

        Assert.Equal(Installer, await File.ReadAllBytesAsync(file.Name));
        Assert.Throws<IOException>(() => File.OpenWrite(file.Name).Dispose());
    }

    [Fact]
    public async Task Download_refuses_an_installer_that_does_not_match()
    {
        var release = AppReleaseParser.Parse(Latest(), Prefix)!;
        var updater = Updater(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent("tampered"u8.ToArray()) }, "0.5.1.0");

        await Assert.ThrowsAsync<PatchLightException>(() => updater.DownloadAsync(release, CancellationToken.None));
        Assert.Empty(Directory.GetFiles(dir, "*", SearchOption.AllDirectories));
    }

    private AppUpdater Updater(Func<HttpRequestMessage, HttpResponseMessage> respond, string current) =>
        new(new HttpClient(new StubHandler(respond)), Version.Parse(current), dir);

    private static HttpResponseMessage Ok(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json) };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(respond(request));
    }
}
