using PatchLight.Core;
using PatchLight.Windows;

namespace PatchLight.App;

// "PatchLight.exe --demo": made-up updates and problems, one of each light, for screenshots and
// for trying the app safely. Nothing here touches the real Windows Update.
public static class DemoData
{
    private static readonly string Page = ReleaseHealthSource.PageUrl("25H2");

    public static (PatchLightService Service, DemoActions Actions) Create()
    {
        var updates = new DemoUpdateClient();
        var holdsPath = Path.Combine(Path.GetTempPath(), "patchlight-demo", "holds.json");
        if (File.Exists(holdsPath)) File.Delete(holdsPath);
        // The demo plays a home PC: problems that only hit business PCs are listed but not counted.
        var service = new PatchLightService(updates, new DemoIssueSource(), new HoldStore(holdsPath), TimeProvider.System, "25H2")
        {
            CountBusinessIssues = false,
        };
        return (service, new DemoActions(service));
    }

    // A made-up news report, so the demo shows what one looks like.
    public static INewsSource News { get; } = new DemoNews();

    private sealed class DemoNews : INewsSource
    {
        public Task<IReadOnlyList<NewsArticle>> GetAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<NewsArticle>>(
        [
            new("Example News", "Windows Defender update KB2267602 reportedly slows down some games",
                "https://github.com/yo-gabriel-portfolio-here/PatchLight#demo", DateTimeOffset.Now.AddDays(-1), ["KB2267602"], true),
        ]);
    }

    private static DateOnly DaysAgo(int days) => DateOnly.FromDateTime(DateTime.Today.AddDays(-days));

    private static KnownIssue Issue(string id, string title, string summary, string kb, string build, int daysAgo,
        IssueStatus status) =>
        new(id, title, summary, kb, Version.Parse(build), DaysAgo(daysAgo), status, null, $"{Page}#{id}msgdesc");

    private sealed class DemoIssueSource : IIssueSource
    {
        public Task<IssueSnapshot> GetAsync(string windowsVersion, CancellationToken ct) =>
            Task.FromResult(new IssueSnapshot(
            [
                Issue("1001", "Some PCs show a black screen after signing in",
                    "After installing this update, some PCs show a black screen for several minutes after sign-in.",
                    "KB5127648", "26200.9711", 5, IssueStatus.Confirmed),
                Issue("1002", "Printing to some USB printers might fail",
                    "A workaround is available: unplug the printer and plug it back in after the restart.",
                    "KB5127001", "26200.9600", 12, IssueStatus.Mitigated),
                Issue("1003", "USB audio devices might stop working",
                    "Some USB headsets and speakers stop playing sound until they are reconnected.",
                    "KB5124008", "26200.9550", 35, IssueStatus.Confirmed),
                Issue("1004", "Domain-joined devices might fail to sign in with Windows Hello",
                    "Some PCs joined to an on-premises Active Directory domain can't sign in with a PIN.",
                    "KB5127648", "26200.9711", 5, IssueStatus.Confirmed) with { LikelyBusinessOnly = true },
            ], DateTimeOffset.UtcNow, FromCache: false));
    }

    private sealed class DemoUpdateClient : IUpdateClient
    {
        private readonly List<PendingUpdate> pending =
        [
            new("demo-1", "KB5127648", "2026-10 Cumulative Update for Windows 11, version 25H2 (KB5127648) (26200.9711)",
                Version.Parse("26200.9711"), DaysAgo(5), false),
            new("demo-2", "KB5127001", "2026-10 Update for Windows 11 (KB5127001)", null, DaysAgo(12), false),
            new("demo-3", "KB890830", "Windows Malicious Software Removal Tool x64 - v5.141 (KB890830)", null, DaysAgo(5), false),
            new("demo-4", "KB2267602", "Security Intelligence Update for Microsoft Defender Antivirus - KB2267602", null,
                DaysAgo(1), false),
        ];

        public Task<IReadOnlyList<PendingUpdate>> GetPendingAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<PendingUpdate>>(pending.ToList());

        public Task SetHiddenAsync(string updateId, bool hidden, CancellationToken ct)
        {
            var i = pending.FindIndex(p => p.UpdateId == updateId);
            if (i >= 0) pending[i] = pending[i] with { IsHidden = hidden };
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<InstalledUpdate>> GetHistoryAsync(int max, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<InstalledUpdate>>(
            [
                new("KB5126010", "2026-09 Cumulative Update Preview for Windows 11 (KB5126010) (26200.9600)",
                    Version.Parse("26200.9600"), DateTimeOffset.Now.AddDays(-14)),
                new("KB5124008", "2026-09 Cumulative Update for Windows 11 (KB5124008) (26200.9550)",
                    Version.Parse("26200.9550"), DateTimeOffset.Now.AddDays(-34)),
                new("KB2267602", "Security Intelligence Update for Microsoft Defender Antivirus - KB2267602", null,
                    DateTimeOffset.Now.AddDays(-2)),
            ]);
    }

    public sealed class DemoActions(PatchLightService service) : IElevatedActions
    {
        private bool dailyCheck;

        public bool DailyCheckOn => dailyCheck;

        public async Task<string?> HoldAsync(string kb, int days, CancellationToken ct)
        {
            await service.HoldAsync(kb, days, "Paused by you", ct);
            dailyCheck = true;
            return null;
        }

        public Task ReleaseAsync(string kb, CancellationToken ct) => service.ReleaseAsync(kb, ct);

        // Nothing is really removed in the demo; it only records the pause that would follow.
        public async Task<string?> UninstallAsync(string kb, int days, CancellationToken ct)
        {
            await service.PlanHoldAsync(kb, days, [], ct);
            dailyCheck = true;
            return null;
        }

        // The demo only remembers the choice; nothing is paused on its own and no policy is written.
        public ProtectionSettings Protection { get; private set; } = new();

        public Task<string?> SetProtectionAsync(ProtectionChoice choice, CancellationToken ct)
        {
            Protection = Protection with { AutoPauseRed = choice.AutoPauseRed, WaitDays = choice.WaitDays, PauseDays = choice.PauseDays };
            if (choice.AutoPauseRed || choice.WaitDays > 0) dailyCheck = true;
            return Task.FromResult<string?>(null);
        }

        public Task EnableDailyCheckAsync(CancellationToken ct)
        {
            dailyCheck = true;
            return Task.CompletedTask;
        }

        public Task DisableDailyCheckAsync(CancellationToken ct)
        {
            dailyCheck = false;
            return Task.CompletedTask;
        }
    }
}
