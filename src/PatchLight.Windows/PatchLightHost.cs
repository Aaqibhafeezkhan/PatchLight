using System.Runtime.Versioning;
using PatchLight.Core;

namespace PatchLight.Windows;

// Wires the engine to the real PC. Shared by the command line and the app.
[SupportedOSPlatform("windows")]
public static class PatchLightHost
{
    public const string ProjectUrl = "https://github.com/yo-gabriel-portfolio-here/PatchLight";

    public static string HoldsPath(string dataDirectory) => Path.Combine(dataDirectory, "holds.json");

    public static string BackgroundLogPath(string dataDirectory) => Path.Combine(dataDirectory, "last-background-check.json");

    public static PatchLightService CreateService(string windowsVersion, string? dataDirectory = null)
    {
        var data = dataDirectory ?? SystemInfo.DataDirectory;
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"PatchLight/0.2 (+{ProjectUrl})");
        var source = new ReleaseHealthSource(http, Path.Combine(data, "cache"), TimeProvider.System);
        return new PatchLightService(new WindowsUpdateClient(), source, new HoldStore(HoldsPath(data)),
            TimeProvider.System, windowsVersion);
    }
}
