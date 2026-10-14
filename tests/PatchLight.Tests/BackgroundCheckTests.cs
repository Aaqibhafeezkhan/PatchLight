using Microsoft.Extensions.Time.Testing;
using PatchLight.Core;
using PatchLight.Windows;
using static PatchLight.Tests.VerdictEngineTests;

namespace PatchLight.Tests;

public sealed class BackgroundCheckTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "patchlight-bg-" + Guid.NewGuid());
    private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 10, 13, 17, 0, 0, TimeSpan.Zero));
    private readonly FakeUpdateClient updates = new();
    private string LogPath => Path.Combine(dir, "last-background-check.json");

    public void Dispose()
    {
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }

    private PatchLightService Service(IIssueSource issues, HoldStore holds) =>
        new(updates, issues, holds, time, "25H2");

    [Fact]
    public async Task Resumes_a_fixed_update_and_logs_it()
    {
        var holds = new HoldStore(Path.Combine(dir, "holds.json"));
        updates.Pending.Add(Update(hidden: true));
        holds.Save([new Hold("KB5127648", "id-KB5127648", time.GetUtcNow().AddDays(-2), time.GetUtcNow().AddDays(12),
            "Paused by you", ["1"])]);

        var log = await BackgroundCheck.RunAsync(Service(new FakeIssueSource(time), holds), LogPath, time, CancellationToken.None);

        Assert.Equal(["KB5127648"], log.Resumed);
        Assert.Null(log.Error);
        Assert.Equal(time.GetUtcNow(), log.RanAt);
        Assert.Contains(("id-KB5127648", false), updates.HideCalls);
        Assert.Equal(log, BackgroundCheck.ReadLog(LogPath));
    }

    [Fact]
    public async Task Failure_is_logged_not_thrown()
    {
        var log = await BackgroundCheck.RunAsync(
            Service(new ThrowingIssueSource(), new HoldStore(Path.Combine(dir, "holds.json"))), LogPath, time, CancellationToken.None);

        Assert.Equal("No internet.", log.Error);
        Assert.Empty(log.Resumed);
        Assert.Equal("No internet.", BackgroundCheck.ReadLog(LogPath)!.Error);
    }

    [Fact]
    public void Missing_or_damaged_log_reads_as_nothing()
    {
        Assert.Null(BackgroundCheck.ReadLog(LogPath));
        Directory.CreateDirectory(dir);
        File.WriteAllText(LogPath, "{ nope");
        Assert.Null(BackgroundCheck.ReadLog(LogPath));
    }

    private sealed class ThrowingIssueSource : IIssueSource
    {
        public Task<IssueSnapshot> GetAsync(string windowsVersion, CancellationToken ct) =>
            throw new PatchLightException("No internet.");
    }
}
