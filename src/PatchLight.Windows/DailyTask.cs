using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using PatchLight.Core;

namespace PatchLight.Windows;

// The scheduled task that runs "PatchLight.exe --background-check" once a day as SYSTEM,
// so fixed updates resume even when nobody opens the app. Changing it needs administrator rights.
[SupportedOSPlatform("windows")]
public static class DailyTask
{
    public const string Name = @"\PatchLight\Daily check";
    public const string Argument = "--background-check";

    public static string BuildXml(string exePath) => $"""
        <?xml version="1.0" encoding="UTF-16"?>
        <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
          <RegistrationInfo>
            <Author>PatchLight</Author>
            <Description>Resumes Windows updates that PatchLight paused once Microsoft fixes them, and ends pauses that ran out.</Description>
          </RegistrationInfo>
          <Triggers>
            <CalendarTrigger>
              <StartBoundary>2026-01-01T10:00:00</StartBoundary>
              <Enabled>true</Enabled>
              <ScheduleByDay>
                <DaysInterval>1</DaysInterval>
              </ScheduleByDay>
            </CalendarTrigger>
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
    public static string? RegisteredExePath()
    {
        var (code, output) = Schtasks("/Query", "/TN", Name, "/XML");
        return code == 0 ? CommandFromXml(output) : null;
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
