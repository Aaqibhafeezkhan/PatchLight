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
    }

    [Fact]
    public void Odd_characters_in_the_path_are_escaped()
    {
        var path = @"C:\Tools & <Stuff>\PatchLight.exe";
        Assert.Equal(path, DailyTask.CommandFromXml(DailyTask.BuildXml(path)));
    }

    [Fact]
    public void Reads_the_command_back_from_task_xml()
    {
        Assert.Equal(@"C:\X\PatchLight.exe", DailyTask.CommandFromXml(DailyTask.BuildXml(@"C:\X\PatchLight.exe")));
        Assert.Null(DailyTask.CommandFromXml("not xml"));
        Assert.Null(DailyTask.CommandFromXml("<Task />"));
    }
}
