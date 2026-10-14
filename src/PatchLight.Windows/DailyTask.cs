using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using PatchLight.Core;

namespace PatchLight.Windows;

// The scheduled task that runs "PatchLight.exe --background-check" every hour as SYSTEM, so fixed updates
// resume even when nobody opens the app, and new ones are paused before Windows gets them ready.
// It keeps its old "Daily check" name so older installs need nothing renamed. Changing it needs administrator rights.
[SupportedOSPlatform("windows")]
public static class DailyTask
{
    public const string Name = @"\PatchLight\Daily check";
    public const string Argument = "--background-check";

    // Run by the installer as administrator: brings an existing task up to date, never creates one.
    public const string RefreshArgument = "--refresh-daily-check";
    private const string Every = "PT1H";

    public static string BuildXml(string exePath) => $"""
        <?xml version="1.0" encoding="UTF-16"?>
        <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
          <RegistrationInfo>
            <Author>PatchLight</Author>
            <Description>Every hour: resumes Windows updates that PatchLight paused once Microsoft fixes them, ends pauses that ran out, and applies PatchLight's protection settings.</Description>
          </RegistrationInfo>
          <Triggers>
            <CalendarTrigger>
              <StartBoundary>2026-01-01T10:00:00</StartBoundary>
              <Enabled>true</Enabled>
              <Repetition>
                <Interval>{Every}</Interval>
                <Duration>P1D</Duration>
                <StopAtDurationEnd>false</StopAtDurationEnd>
              </Repetition>
              <ScheduleByDay>
                <DaysInterval>1</DaysInterval>
              </ScheduleByDay>
            </CalendarTrigger>
            <BootTrigger>
              <Enabled>true</Enabled>
              <Delay>PT5M</Delay>
            </BootTrigger>
          </Triggers>
          <Principals>
            <Principal id="Author">
              <UserId>S-1-5-18</UserId>
              <RunLevel>HighestAvailable</RunLevel>
            </Principal>
          </Principals>
          <Settings>
            <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
            <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
            <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
            <StartWhenAvailable>true</StartWhenAvailable>
            <RunOnlyIfNetworkAvailable>true</RunOnlyIfNetworkAvailable>
            <ExecutionTimeLimit>PT30M</ExecutionTimeLimit>
            <Enabled>true</Enabled>
          </Settings>
          <Actions Context="Author">
            <Exec>
              <Command>{SecurityElement.Escape(exePath)}</Command>
              <Arguments>{Argument}</Arguments>
            </Exec>
          </Actions>
        </Task>
        """;

    // Tasks made by PatchLight 0.4 and older run once a day.
    public static bool IsCurrent(string xml)
    {
        try
        {
            return XDocument.Parse(xml).Descendants().Any(e => e.Name.LocalName == "Interval" && e.Value == Every);
        }
        catch (XmlException)
        {
            return false;
        }
    }

    // Missing, pointing at another copy of PatchLight, or from an older version.
    public static bool NeedsRegister(string exePath)
    {
        var xml = RegisteredXml();
        return xml is null || !string.Equals(CommandFromXml(xml), exePath, StringComparison.OrdinalIgnoreCase) || !IsCurrent(xml);
    }

    // Brings an existing task up to date; does nothing when there is none.
    public static void Refresh(string exePath)
    {
        if (RegisteredXml() is not null) Register(exePath);
    }

    public static string? CommandFromXml(string xml)
    {
        try
        {
            return XDocument.Parse(xml).Descendants().FirstOrDefault(e => e.Name.LocalName == "Command")?.Value;
        }
        catch (XmlException)
        {
            return null;
        }
    }

    public static void Register(string exePath)
    {
        if (!InstallLocation.IsAdminOnly(exePath))
            throw new PatchLightException(
                "The daily check runs PatchLight with full system rights, so PatchLight must be in a folder only " +
                @"administrators can change. Move the PatchLight folder to C:\Program Files and try again.");
        var file = Path.Combine(Path.GetTempPath(), $"patchlight-task-{Guid.NewGuid():N}.xml");
        try
        {
            // schtasks wants the file in UTF-16, matching the XML declaration.
            File.WriteAllText(file, BuildXml(exePath), Encoding.Unicode);
            if (Schtasks("/Create", "/TN", Name, "/XML", file, "/F").ExitCode != 0)
                throw new PatchLightException("Couldn't turn on the daily check.");
        }
        finally
        {
            File.Delete(file);
        }
    }

    public static void Unregister()
    {
        if (RegisteredExePath() is null) return;
        if (Schtasks("/Delete", "/TN", Name, "/F").ExitCode != 0)
            throw new PatchLightException("Couldn't turn off the daily check.");
    }

    // The program the task runs, or null when the task doesn't exist.
    public static string? RegisteredExePath() => RegisteredXml() is { } xml ? CommandFromXml(xml) : null;

    // The task as Task Scheduler has it, or null when it doesn't exist.
    public static string? RegisteredXml()
    {
        var (code, output) = Schtasks("/Query", "/TN", Name, "/XML");
        return code == 0 ? output : null;
    }

    private static (int ExitCode, string Output) Schtasks(params string[] args)
    {
        var start = new ProcessStartInfo("schtasks.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)
            ?? throw new PatchLightException("Couldn't start the Windows Task Scheduler.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        _ = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return (process.ExitCode, stdout.Result);
    }
}
