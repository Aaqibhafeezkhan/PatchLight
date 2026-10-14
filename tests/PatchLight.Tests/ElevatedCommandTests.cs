using PatchLight.Core;
using PatchLight.Windows;

namespace PatchLight.Tests;

public sealed class ElevatedCommandTests : IDisposable
{
    private readonly string resultPath = Path.Combine(Path.GetTempPath(), "patchlight-result-" + Guid.NewGuid() + ".txt");

    public void Dispose() => File.Delete(resultPath);

    private string[] WithResult(IReadOnlyList<string> args) => [.. args, "--result", resultPath];

    [Fact]
    public void Parses_hold_with_days()
    {
        var command = ElevatedCommand.Parse(WithResult(ElevatedCommand.HoldArgs("KB5127648", 21)));
        Assert.Equal(new ElevatedCommand("--hold", "KB5127648", 21, resultPath), command);
    }

    [Fact]
    public void Hold_days_default_and_stay_in_range()
    {
        Assert.Equal(HoldPolicy.DefaultDays, ElevatedCommand.Parse(["--hold", "KB5127648", "--result", resultPath])!.Days);
        Assert.Equal(HoldPolicy.MaxDays, ElevatedCommand.Parse(["--hold", "KB5127648", "--days", "400", "--result", resultPath])!.Days);
    }

    [Fact]
    public void Parses_the_other_commands()
    {
        Assert.Equal("--release", ElevatedCommand.Parse(WithResult(ElevatedCommand.ReleaseArgs("KB5127648")))!.Verb);
        Assert.Equal("--enable-daily-check", ElevatedCommand.Parse(WithResult(ElevatedCommand.EnableDailyCheckArgs))!.Verb);
        Assert.Equal("--disable-daily-check", ElevatedCommand.Parse(WithResult(ElevatedCommand.DisableDailyCheckArgs))!.Verb);
    }

    [Theory]
    [InlineData]
    [InlineData("--tray")]
    [InlineData("--hold", "KB5127648")]
    [InlineData("--hold", "--result", "x.txt")]
    [InlineData("--release", "--result", "x.txt")]
    [InlineData("--hold", "KB5127648", "--days", "lots", "--result", "x.txt")]
    public void Anything_else_is_not_an_elevated_command(params string[] args) =>
        Assert.Null(ElevatedCommand.Parse(args));

    [Fact]
    public async Task Success_writes_an_empty_result()
    {
        var actions = new FakeActions();
        var code = await ElevatedCommand.Parse(WithResult(ElevatedCommand.HoldArgs("KB5127648", 7)))!
            .RunAsync(actions, CancellationToken.None);

        Assert.Equal(0, code);
        Assert.Equal("", File.ReadAllText(resultPath));
        Assert.Equal(["hold KB5127648 7"], actions.Calls);
    }

    [Fact]
    public async Task Warning_is_passed_back_with_success()
    {
        var actions = new FakeActions { Warning = "The daily check couldn't be turned on." };
        var code = await ElevatedCommand.Parse(WithResult(ElevatedCommand.HoldArgs("KB5127648", 7)))!
            .RunAsync(actions, CancellationToken.None);

        Assert.Equal(0, code);
        Assert.Equal("warning:The daily check couldn't be turned on.", File.ReadAllText(resultPath));
    }

    [Theory]
    [InlineData("", 0, null)]
    [InlineData("warning:Careful.", 0, "Careful.")]
    public void Reading_a_good_result(string content, int exitCode, string? warning) =>
        Assert.Equal(warning, Elevation.ReadResult(content, exitCode));

    [Theory]
    [InlineData("KB5127648 isn't paused by PatchLight.", 2, "KB5127648 isn't paused by PatchLight.")]
    [InlineData(null, 0, "Something went wrong.")]
    [InlineData(null, -1, "Something went wrong.")]
    public void Reading_a_failed_result(string? content, int exitCode, string message) =>
        Assert.Equal(message, Assert.Throws<PatchLightException>(() => Elevation.ReadResult(content, exitCode)).Message);

    [Fact]
    public async Task Plain_failure_message_is_passed_back()
    {
        var actions = new FakeActions { Failure = new PatchLightException("KB5127648 isn't paused by PatchLight.") };
        var code = await ElevatedCommand.Parse(WithResult(ElevatedCommand.ReleaseArgs("KB5127648")))!
            .RunAsync(actions, CancellationToken.None);

        Assert.Equal(2, code);
        Assert.Equal("KB5127648 isn't paused by PatchLight.", File.ReadAllText(resultPath));
    }

    [Fact]
    public async Task Unexpected_failure_says_something_went_wrong()
    {
        var actions = new FakeActions { Failure = new InvalidOperationException("boom") };
        var code = await ElevatedCommand.Parse(WithResult(ElevatedCommand.EnableDailyCheckArgs))!
            .RunAsync(actions, CancellationToken.None);

        Assert.Equal(2, code);
        Assert.Equal("Something went wrong.", File.ReadAllText(resultPath));
    }

    private sealed class FakeActions : IElevatedActions
    {
        public List<string> Calls { get; } = [];
        public Exception? Failure { get; init; }
        public string? Warning { get; init; }

        private Task Record(string call)
        {
            if (Failure is not null) throw Failure;
            Calls.Add(call);
            return Task.CompletedTask;
        }

        public async Task<string?> HoldAsync(string kb, int days, CancellationToken ct)
        {
            await Record($"hold {kb} {days}");
            return Warning;
        }

        public Task ReleaseAsync(string kb, CancellationToken ct) => Record($"release {kb}");
        public Task EnableDailyCheckAsync(CancellationToken ct) => Record("enable");
        public Task DisableDailyCheckAsync(CancellationToken ct) => Record("disable");
    }
}
