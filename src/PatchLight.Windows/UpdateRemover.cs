using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using PatchLight.Core;

namespace PatchLight.Windows;

public enum RemovalMethod { Dism, Wusa }

// Removes an installed update. Windows cumulative updates come bundled with a servicing stack update,
// which wusa can't remove, so those go through DISM by package name. Everything else uses wusa.
[SupportedOSPlatform("windows")]
public static partial class UpdateRemover
{
    private const int RestartNeeded = 3010;
    private const int NotInstalled = 0x240007;

    [GeneratedRegex(@"Package Identity\s*:\s*(\S+)\s+State\s*:\s*(\w+)", RegexOptions.IgnoreCase)]
    private static partial Regex PackageEntry();

    public static RemovalMethod MethodFor(InstalledUpdate update) => update.Build is null ? RemovalMethod.Wusa : RemovalMethod.Dism;

    public static bool Succeeded(int exitCode) => exitCode is 0 or RestartNeeded;

    // Cumulative update packages are named "Package_for_RollupFix~...~26100.9711.1.6". The base build
    // can differ from the one Windows Update shows (26200 runs on 26100), so match on the revision.
    public static string? FindRollupPackage(string dismOutput, Version build) =>
        PackageEntry().Matches(dismOutput)
            .Where(m => m.Groups[1].Value.StartsWith("Package_for_RollupFix~", StringComparison.OrdinalIgnoreCase)
                && m.Groups[2].Value.Equals("Installed", StringComparison.OrdinalIgnoreCase))
            .Select(m => m.Groups[1].Value)
            .FirstOrDefault(name => Version.TryParse(name[(name.LastIndexOf('~') + 1)..], out var v) && v.Minor == build.Minor);

    // Needs administrator rights. Windows finishes removing the update at the next restart.
    public static void Remove(InstalledUpdate update)
    {
        var kb = update.Kb ?? throw new PatchLightException("This update has no KB number, so PatchLight can't remove it.");
        int code;
        if (MethodFor(update) == RemovalMethod.Dism)
        {
            var (listCode, list) = Run("dism.exe", "/online", "/get-packages", "/english");
            if (listCode != 0) throw new PatchLightException($"Windows couldn't list its installed updates (error {listCode}).");
            var package = FindRollupPackage(list, update.Build!) ?? throw new PatchLightException(
                $"Windows can't remove {kb} anymore because a newer update replaced it. " +
                "To go back further, use Settings > System > Recovery.");
            code = Run("dism.exe", "/online", "/remove-package", $"/packagename:{package}", "/quiet", "/norestart").ExitCode;
        }
        else
        {
            code = Run("wusa.exe", "/uninstall", $"/kb:{kb[2..]}", "/quiet", "/norestart").ExitCode;
            if (code == NotInstalled) throw new PatchLightException($"{kb} isn't installed on this PC.");
        }
        if (!Succeeded(code)) throw new PatchLightException($"Windows couldn't remove {kb} (error {code}).");
    }

    private static (int ExitCode, string Output) Run(string exe, params string[] args)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, exe))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new PatchLightException($"Couldn't start {exe}.");
        var output = process.StandardOutput.ReadToEndAsync();
        _ = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return (process.ExitCode, output.Result);
    }
}
