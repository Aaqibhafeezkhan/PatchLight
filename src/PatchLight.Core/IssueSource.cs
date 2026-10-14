namespace PatchLight.Core;

public interface IIssueSource
{
    Task<IssueSnapshot> GetAsync(string windowsVersion, CancellationToken ct);
}

public sealed class ReleaseHealthSource(HttpClient http, string cacheDir, TimeProvider time) : IIssueSource
{
    public static string PageUrl(string windowsVersion) =>
        $"https://learn.microsoft.com/en-us/windows/release-health/status-windows-11-{windowsVersion.ToLowerInvariant()}";

    public async Task<IssueSnapshot> GetAsync(string windowsVersion, CancellationToken ct)
    {
        var url = PageUrl(windowsVersion);
        var cachePath = Path.Combine(cacheDir, $"release-health-{windowsVersion.ToLowerInvariant()}.html");
        try
        {
            using var response = await http.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();
            var html = await response.Content.ReadAsStringAsync(ct);
            var issues = ReleaseHealthParser.Parse(html, url);

            var now = time.GetUtcNow();
            Directory.CreateDirectory(cacheDir);
            await File.WriteAllTextAsync(cachePath, html, ct);
            File.SetLastWriteTimeUtc(cachePath, now.UtcDateTime);
            return new IssueSnapshot(issues, now, FromCache: false);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested
            && ex is HttpRequestException or TaskCanceledException or ReleaseHealthFormatException)
        {
            if (!File.Exists(cachePath))
                throw new PatchLightException(
                    "Couldn't reach Microsoft's known issues list, and there is no saved copy yet. " +
                    "Check your internet connection and try again.");

            var issues = ReleaseHealthParser.Parse(await File.ReadAllTextAsync(cachePath, ct), url);
            var savedAt = new DateTimeOffset(File.GetLastWriteTimeUtc(cachePath), TimeSpan.Zero);
            return new IssueSnapshot(issues, savedAt, FromCache: true);
        }
    }
}
