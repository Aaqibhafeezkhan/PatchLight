using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using PatchLight.Core;

namespace PatchLight.Windows;

public sealed record AppUpdateCheck(bool Reached, AppRelease? Newer);

// Finds a newer PatchLight on GitHub and installs it when the person asks. Nothing installs on its own:
// the installer only runs after a click, and Windows asks for administrator permission first.
[SupportedOSPlatform("windows")]
public sealed class AppUpdater(HttpClient http, Version current, string downloadDirectory)
{
    public const string LatestUrl = "https://api.github.com/repos/G4SBO/PatchLight/releases/latest";
    public const string DownloadPrefix = PatchLightHost.ProjectUrl + "/releases/download/";

    // Tells the new installer to open PatchLight again when it's done.
    public const string InstallerArguments = "/SILENT /SP- /CLOSEAPPLICATIONS /RELAUNCH";

    private const int Cancelled = 1223; // ERROR_CANCELLED: the person said no to the permission prompt.

    public Version Current { get; } = current;

    // Checking never fails loudly: Reached is false when GitHub couldn't be asked, Newer is null when up to date.
    public async Task<AppUpdateCheck> CheckAsync(CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestUrl);
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return new(false, null);
            var release = AppReleaseParser.Parse(await response.Content.ReadAsStringAsync(ct), DownloadPrefix);
            return new(true, release is not null && AppReleaseParser.IsNewer(release, Current) ? release : null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            return new(false, null);
        }
    }

    // Downloads the installer into a new folder and checks it against the SHA-256 GitHub listed. The file stays
    // open and locked against changes, so nothing can swap it between the check and the install.
    public async Task<FileStream> DownloadAsync(AppRelease release, CancellationToken ct)
    {
        var folder = Path.Combine(downloadDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, AppReleaseParser.SetupName);

        FileStream? file = null;
        try
        {
            await using (var download = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var response = await http.GetAsync(release.SetupUrl, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                if (!response.IsSuccessStatusCode)
                    throw new PatchLightException("Couldn't download the update. Try again later.");
                await using var body = await response.Content.ReadAsStreamAsync(ct);
                await body.CopyToAsync(download, ct);
            }
            // Read-only from here, so Windows can still start it; checked only after it's locked.
            file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(file, ct));
            if (hash != release.Sha256)
                throw new PatchLightException("The downloaded update didn't match the one published on GitHub, so it wasn't installed.");
            return file;
        }
        catch
        {
            if (file is not null) await file.DisposeAsync();
            TryDelete(folder);
            throw;
        }
    }

    // False when the person said no to Windows' permission prompt.
    public static bool StartInstaller(FileStream installer)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(installer.Name, InstallerArguments)
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = Path.GetDirectoryName(installer.Name)!,
            });
            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == Cancelled)
        {
            return false;
        }
    }

    // Installers from earlier updates; they're only needed while they run.
    public void CleanUp()
    {
        try
        {
            if (Directory.Exists(downloadDirectory))
                foreach (var folder in Directory.GetDirectories(downloadDirectory)) TryDelete(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void TryDelete(string folder)
    {
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
