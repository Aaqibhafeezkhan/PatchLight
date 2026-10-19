using System.Text.Json;
using System.Text.RegularExpressions;

namespace PatchLight.Core;

// A published PatchLight release: its installer and the SHA-256 GitHub lists for it.
public sealed record AppRelease(Version Version, string PageUrl, string SetupUrl, string Sha256);

// Reads GitHub's "latest release" answer (api.github.com/repos/{owner}/{repo}/releases/latest).
public static partial class AppReleaseParser
{
    public const string SetupName = "PatchLight-Setup.exe";

    [GeneratedRegex(@"^v?(\d+)\.(\d+)\.(\d+)$")]
    private static partial Regex Tag();

    [GeneratedRegex(@"^sha256:([0-9a-f]{64})$", RegexOptions.IgnoreCase)]
    private static partial Regex Digest();

    // downloadPrefix: the installer must come from this project's own releases, nowhere else.
    public static AppRelease? Parse(string json, string downloadPrefix)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (Bool(root, "draft") || Bool(root, "prerelease")) return null;
            if (ParseVersion(Text(root, "tag_name")) is not { } version) return null;
            if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return null;

            foreach (var asset in assets.EnumerateArray())
            {
                if (Text(asset, "name") != SetupName) continue;
                var url = Text(asset, "browser_download_url");
                var digest = Digest().Match(Text(asset, "digest") ?? "");
                if (url is null || !url.StartsWith(downloadPrefix, StringComparison.Ordinal) || !digest.Success) return null;
                return new AppRelease(version, Text(root, "html_url") ?? url, url, digest.Groups[1].Value.ToLowerInvariant());
            }
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static Version? ParseVersion(string? tag) =>
        tag is not null && Tag().Match(tag.Trim()) is { Success: true } m
            ? new Version(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value))
            : null;

    // Assembly versions carry a fourth part (0.5.1.0); only the first three count.
    public static bool IsNewer(AppRelease release, Version current) =>
        release.Version > new Version(current.Major, current.Minor, Math.Max(0, current.Build));

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool Bool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}
