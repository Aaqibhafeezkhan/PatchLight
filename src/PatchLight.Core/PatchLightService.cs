namespace PatchLight.Core;

public sealed class PatchLightService(
    IUpdateClient updates,
    IIssueSource issues,
    HoldStore holds,
    TimeProvider time,
    string windowsVersion)
{
    // Off on home PCs: problems that usually only hit business PCs are listed but don't color the light.
    public bool CountBusinessIssues { get; set; } = true;

    public async Task<CheckResult> CheckAsync(bool applyAutoRelease, CancellationToken ct)
    {
        var data = await issues.GetAsync(windowsVersion, ct);
        var pending = await updates.GetPendingAsync(ct);
        var now = time.GetUtcNow();
        var upgrades = await UpgradeIssuesAsync(pending, ct);
        var verdicts = pending.Select(u => Judge(u, data.Issues, upgrades)).ToList();
        var open = data.Issues.Concat(upgrades.Values.SelectMany(i => i ?? []))
            .Where(i => i.Status != IssueStatus.Resolved).Select(i => i.Id).ToHashSet();

        var active = new List<Hold>();
        var released = new List<ReleasedHold>();
        var attached = false;
        foreach (var stored in holds.Load())
        {
            var hold = stored;
            if (hold.IsWaiting)
            {
                if (now >= hold.ExpiresOn)
                {
                    released.Add(new ReleasedHold(hold, ReleaseReason.Expired));
                    continue;
                }
                var offered = verdicts.FindIndex(v => !v.Update.IsFeatureUpdate && v.Update.Kb == hold.Kb);
                if (offered >= 0 && applyAutoRelease)
                {
                    var update = verdicts[offered].Update;
                    if (!update.IsHidden) await updates.SetHiddenAsync(update.UpdateId, true, ct);
                    verdicts[offered] = verdicts[offered] with { Update = update with { IsHidden = true } };
                    hold = hold with { UpdateId = update.UpdateId };
                    attached = true;
                }
                active.Add(hold);
                continue;
            }

            var verdict = verdicts.FirstOrDefault(v => v.Update.UpdateId == hold.UpdateId);
            // Without the new version's page we can't tell whether its problems were fixed.
            var unknown = verdict?.Update.IsFeatureUpdate == true && upgrades[verdict.Update.TargetVersion!] is null;
            ReleaseReason? reason =
                verdict is null ? ReleaseReason.Gone
                : now >= hold.ExpiresOn ? ReleaseReason.Expired
                : unknown ? null
                // Decided by the problems recorded with the pause, not by the light, so the business setting
                // (which the app and the daily check may see differently) can't end a pause early.
                : hold.IssueIds.Count > 0 && !hold.IssueIds.Any(open.Contains) && verdict.Light != Light.Red ? ReleaseReason.Fixed
                : null;

            if (reason is null)
            {
                active.Add(hold);
                continue;
            }
            if (applyAutoRelease && verdict is not null)
                await updates.SetHiddenAsync(hold.UpdateId, false, ct);
            released.Add(new ReleasedHold(hold, reason.Value));
        }

        if (applyAutoRelease && (released.Count > 0 || attached))
            holds.Save(active);

        return new CheckResult(verdicts, active, released, applyAutoRelease, data, now);
    }

    // What a check does that doesn't need Microsoft's list: end pauses whose time ran out, and pause updates
    // the person removed as soon as Windows offers them again. Used when a full check can't run.
    // Returns the pauses it ended.
    public async Task<IReadOnlyList<Hold>> ReleaseExpiredAsync(CancellationToken ct)
    {
        var all = holds.Load();
        var now = time.GetUtcNow();
        var expired = all.Where(h => now >= h.ExpiresOn).ToList();
        var waiting = all.Where(h => h.IsWaiting && now < h.ExpiresOn).ToList();
        if (expired.Count == 0 && waiting.Count == 0) return [];

        var pending = await updates.GetPendingAsync(ct);
        foreach (var hold in expired.Where(h => pending.Any(u => u.UpdateId == h.UpdateId)))
            await updates.SetHiddenAsync(hold.UpdateId, false, ct);

        var kept = new List<Hold>();
        var changed = expired.Count > 0;
        foreach (var hold in all.Except(expired))
        {
            var offered = hold.IsWaiting ? pending.FirstOrDefault(u => !u.IsFeatureUpdate && u.Kb == hold.Kb) : null;
            if (offered is null)
            {
                kept.Add(hold);
                continue;
            }
            if (!offered.IsHidden) await updates.SetHiddenAsync(offered.UpdateId, true, ct);
            kept.Add(hold with { UpdateId = offered.UpdateId });
            changed = true;
        }
        if (changed) holds.Save(kept);
        return expired;
    }

    // Used when PatchLight is uninstalled: nothing it paused may stay hidden once it's gone.
    public async Task<IReadOnlyList<Hold>> ReleaseAllAsync(CancellationToken ct)
    {
        var all = holds.Load();
        if (all.Count == 0) return [];

        var pending = await updates.GetPendingAsync(ct);
        foreach (var hold in all.Where(h => pending.Any(u => u.UpdateId == h.UpdateId)))
            await updates.SetHiddenAsync(hold.UpdateId, false, ct);
        holds.Save([]);
        return all;
    }

    // After PatchLight removes an update, Windows offers it again. This records a pause that is applied
    // as soon as that happens (by the next check that runs as administrator), and that ends on time like any pause.
    public Task<Hold> PlanHoldAsync(string kbText, int days, IReadOnlyList<string> issueIds, CancellationToken ct)
    {
        var kb = RequireKb(kbText);
        var now = time.GetUtcNow();
        var hold = new Hold(kb, "", now, now.AddDays(Math.Clamp(days, 1, HoldPolicy.MaxDays)), "Removed by you", issueIds);
        holds.Save([.. holds.Load().Where(h => h.Kb != kb), hold]);
        return Task.FromResult(hold);
    }

    public async Task<Hold> HoldAsync(string kbText, int days, string reason, CancellationToken ct)
    {
        var kb = RequireKey(kbText);
        var update = (await updates.GetPendingAsync(ct)).FirstOrDefault(u => u.PauseKey == kb)
            ?? throw new PatchLightException($"{kb} isn't waiting to be installed on this PC, so there is nothing to pause.");

        IReadOnlyList<string> issueIds;
        try
        {
            var data = await issues.GetAsync(update.TargetVersion ?? windowsVersion, ct);
            var verdict = update.IsFeatureUpdate
                ? VerdictEngine.JudgeUpgrade(update, data.Issues, CountBusinessIssues)
                : VerdictEngine.Judge(update, data.Issues, Today(), CountBusinessIssues);
            issueIds = verdict.Issues.Concat(verdict.SetAside).Select(i => i.Id).ToList();
        }
        catch (PatchLightException)
        {
            issueIds = [];
        }

        await updates.SetHiddenAsync(update.UpdateId, true, ct);

        var now = time.GetUtcNow();
        var hold = new Hold(kb, update.UpdateId, now, now.AddDays(Math.Clamp(days, 1, HoldPolicy.MaxDays)), reason, issueIds);
        try
        {
            holds.Save([.. holds.Load().Where(h => h.Kb != kb), hold]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Without a saved pause nothing would ever resume it, so put the update back.
            try { await updates.SetHiddenAsync(update.UpdateId, false, CancellationToken.None); }
            catch (Exception) { }
            throw new PatchLightException("Couldn't save the pause, so the update was left as it was. Try again.");
        }
        return hold;
    }

    public async Task<ReleasedHold> ReleaseAsync(string kbText, CancellationToken ct)
    {
        var kb = RequireKey(kbText);
        var all = holds.Load();
        var hold = all.FirstOrDefault(h => h.Kb == kb)
            ?? throw new PatchLightException($"{kb} isn't paused by PatchLight.");

        if ((await updates.GetPendingAsync(ct)).Any(u => u.UpdateId == hold.UpdateId))
            await updates.SetHiddenAsync(hold.UpdateId, false, ct);

        holds.Save([.. all.Where(h => h.Kb != kb)]);
        return new ReleasedHold(hold, ReleaseReason.Manual);
    }

    public async Task<InstalledUpdate> GetInstalledAsync(string kbText, CancellationToken ct)
    {
        var kb = RequireKb(kbText);
        return (await updates.GetHistoryAsync(500, ct)).Where(u => u.Kb == kb).MaxBy(u => u.InstalledOn)
            ?? throw new PatchLightException($"{kb} isn't installed on this PC.");
    }

    public async Task<IReadOnlyList<HistoryItem>> HistoryAsync(int days, CancellationToken ct)
    {
        var data = await issues.GetAsync(windowsVersion, ct);
        var cutoff = time.GetUtcNow().AddDays(-days);
        var installed = (await updates.GetHistoryAsync(500, ct))
            .Where(u => u.Kb is not null && u.InstalledOn >= cutoff)
            .GroupBy(u => u.Kb)
            .Select(g => g.MaxBy(u => u.InstalledOn)!)
            .OrderBy(u => u.InstalledOn)
            .ToList();

        // Blame the update that brought each open problem: the exact KB if installed,
        // otherwise the first later update on the same build line.
        var blame = new Dictionary<string, List<KnownIssue>>();
        foreach (var issue in data.Issues.Where(i => i.Status != IssueStatus.Resolved))
        {
            var culprit = installed.FirstOrDefault(u => u.Kb == issue.OriginatingKb)
                ?? installed.FirstOrDefault(u => VerdictEngine.Affects(issue, kb: null, u.Build));
            if (culprit is null) continue;
            if (!blame.TryGetValue(culprit.Kb!, out var list)) blame[culprit.Kb!] = list = [];
            list.Add(issue);
        }

        return installed
            .OrderByDescending(u => u.InstalledOn)
            .Select(u => new HistoryItem(u, blame.GetValueOrDefault(u.Kb!) ?? []))
            .ToList();
    }

    private DateOnly Today() => DateOnly.FromDateTime(time.GetLocalNow().DateTime);

    private UpdateVerdict Judge(PendingUpdate update, IReadOnlyList<KnownIssue> current,
        Dictionary<string, IReadOnlyList<KnownIssue>?> upgrades) =>
        !update.IsFeatureUpdate ? VerdictEngine.Judge(update, current, Today(), CountBusinessIssues)
        : upgrades[update.TargetVersion!] is { } target ? VerdictEngine.JudgeUpgrade(update, target, CountBusinessIssues)
        : VerdictEngine.UpgradeUnknown(update);

    // Each offered upgrade is judged against its own version's page. One that can't be read becomes "wait",
    // not a failed check.
    private async Task<Dictionary<string, IReadOnlyList<KnownIssue>?>> UpgradeIssuesAsync(
        IReadOnlyList<PendingUpdate> pending, CancellationToken ct)
    {
        var result = new Dictionary<string, IReadOnlyList<KnownIssue>?>(StringComparer.OrdinalIgnoreCase);
        foreach (var version in pending.Where(u => u.IsFeatureUpdate).Select(u => u.TargetVersion!).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                result[version] = (await issues.GetAsync(version, ct)).Issues;
            }
            catch (PatchLightException)
            {
                result[version] = null;
            }
        }
        return result;
    }

    // A KB number, or a Windows version like 26H2 for an upgrade.
    private static string RequireKey(string text) =>
        KbNumber.Normalize(text)
        ?? (System.Text.RegularExpressions.Regex.IsMatch(text.Trim(), @"^\d{2}[Hh]\d$") ? text.Trim().ToUpperInvariant() : null)
        ?? throw new PatchLightException($"'{text}' isn't an update number. Use one like KB5127648, or a Windows version like 26H2.");

    private static string RequireKb(string text) =>
        KbNumber.Normalize(text)
        ?? throw new PatchLightException($"'{text}' isn't an update number. Use one like KB5127648.");
}
