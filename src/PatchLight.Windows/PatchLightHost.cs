using System.Runtime.Versioning;
using PatchLight.Core;

namespace PatchLight.Windows;

// Wires the engine to the real PC. Shared by the command line and the app.
[SupportedOSPlatform("windows")]
public static class PatchLightHost
{
    public const string ProjectUrl = "https://github.com/G4SBO/PatchLight";
    public const string AuthorUrl = "https://g4sbo.com";

    public static string HoldsPath(string dataDirectory) => Path.Combine(dataDirectory, "holds.json");

    public static string BackgroundLogPath(string dataDirectory) => Path.Combine(dataDirectory, "last-background-check.json");

    // Protection settings: read by the background check, written only by the administrator helper.
    public static ProtectionStore CreateProtectionStore(string? dataDirectory = null) =>
        new(Path.Combine(dataDirectory ?? SystemInfo.DataDirectory, "settings.json"));

    // Tech-news reports are kept per person, next to their settings.
    public static INewsSource CreateNewsSource() => new RssNewsSource(NewHttpClient(),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PatchLight", "news.json"),
        TimeProvider.System, RssNewsSource.DefaultFeeds);

    // The installer is about 50 MB, so downloads get much longer than a page read.
    public static AppUpdater CreateAppUpdater(Version current) => new(NewHttpClient(TimeSpan.FromMinutes(15)), current,
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PatchLight", "updates"));

    private static HttpClient NewHttpClient(TimeSpan? timeout = null)
    {
        var http = new HttpClient { Timeout = timeout ?? TimeSpan.FromSeconds(20) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"PatchLight/0.5 (+{ProjectUrl})");
        return http;
    }

    public static PatchLightService CreateService(string windowsVersion, string? dataDirectory = null)
    {
        var data = dataDirectory ?? SystemInfo.DataDirectory;
        var source = new ReleaseHealthSource(NewHttpClient(), Path.Combine(data, "cache"), TimeProvider.System);
        return new PatchLightService(new WindowsUpdateClient(), source, new HoldStore(HoldsPath(data)),
            TimeProvider.System, windowsVersion)
        {
            // Home PCs set aside problems that usually only hit business PCs; the app lets people change this.
            CountBusinessIssues = SystemInfo.IsWorkPc(),
        };
    }
}
