using System.Xml.Linq;
using PatchLight.Windows;

namespace PatchLight.Tests;

public class DailyTaskTests
{
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    [Fact]
    public void Task_runs_the_app_daily_as_system()
    {
        var xml = XDocument.Parse(DailyTask.BuildXml(@"C:\Apps\PatchLight\PatchLight.exe"));

        Assert.Equal("S-1-5-18", xml.Descendants(Ns + "UserId").Single().Value);
        Assert.Equal("HighestAvailable", xml.Descendants(Ns + "RunLevel").Single().Value);
        Assert.Single(xml.Descendants(Ns + "ScheduleByDay"));
        Assert.EndsWith("T10:00:00", xml.Descendants(Ns + "StartBoundary").Single().Value);
        Assert.Equal("true", xml.Descendants(Ns + "StartWhenAvailable").Single().Value);
        Assert.Equal("true", xml.Descendants(Ns + "RunOnlyIfNetworkAvailable").Single().Value);
        Assert.Equal("false", xml.Descendants(Ns + "DisallowStartIfOnBatteries").Single().Value);
        Assert.Equal("PT30M", xml.Descendants(Ns + "ExecutionTimeLimit").Single().Value);
        Assert.Equal(@"C:\Apps\PatchLight\PatchLight.exe", xml.Descendants(Ns + "Command").Single().Value);
        Assert.Equal("--background-check", xml.Descendants(Ns + "Arguments").Single().Value);
        // Also soon after start-up, so an update PatchLight removed is paused before Windows reinstalls it.
        Assert.Equal("PT5M", xml.Descendants(Ns + "BootTrigger").Single().Element(Ns + "Delay")!.Value);
    }

    [Fact]
    public void Task_repeats_every_hour()
    {
        var xml = XDocument.Parse(DailyTask.BuildXml(@"C:\Apps\PatchLight\PatchLight.exe"));
        var repetition = xml.Descendants(Ns + "CalendarTrigger").Single().Element(Ns + "Repetition")!;
        Assert.Equal("PT1H", repetition.Element(Ns + "Interval")!.Value);
        Assert.Equal("P1D", repetition.Element(Ns + "Duration")!.Value);
    }

    [Fact]
    public void Task_from_an_older_version_is_not_current()
    {
        Assert.True(DailyTask.IsCurrent(DailyTask.BuildXml(@"C:\X\PatchLight.exe")));
        var old = DailyTask.BuildXml(@"C:\X\PatchLight.exe").Replace("<Interval>PT1H</Interval>", "");
        Assert.False(DailyTask.IsCurrent(old));
        Assert.False(DailyTask.IsCurrent("not xml"));
    }

    [Fact]
    public void Odd_characters_in_the_path_are_escaped()
    {
        var path = @"C:\Tools & <Stuff>\PatchLight.exe";
        Assert.Equal(path, DailyTask.CommandFromXml(DailyTask.BuildXml(path)));
    }

    [Fact]
    public void Refuses_to_run_a_program_normal_users_could_replace()
    {
        var dir = Path.Combine(Path.GetTempPath(), "patchlight-task-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        try
        {
            var exe = Path.Combine(dir, "PatchLight.exe");
            File.WriteAllText(exe, "");
            var ex = Assert.Throws<PatchLight.Core.PatchLightException>(() => DailyTask.Register(exe));
            Assert.Contains(@"C:\Program Files", ex.Message);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Reads_the_command_back_from_task_xml()
    {
        Assert.Equal(@"C:\X\PatchLight.exe", DailyTask.CommandFromXml(DailyTask.BuildXml(@"C:\X\PatchLight.exe")));
        Assert.Null(DailyTask.CommandFromXml("not xml"));
        Assert.Null(DailyTask.CommandFromXml("<Task />"));
    }
}
