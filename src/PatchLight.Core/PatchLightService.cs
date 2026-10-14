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
        var verdicts = pending.Select(u => VerdictEngine.Judge(u, data.Issues, Today(), CountBusinessIssues)).ToList();

        var active = new List<Hold>();
        var released = new List<ReleasedHold>();
        foreach (var hold in holds.Load())
        {
            var verdict = verdicts.FirstOrDefault(v => v.Update.UpdateId == hold.UpdateId);
            ReleaseReason? reason =
                verdict is null ? ReleaseReason.Gone
                : now >= hold.ExpiresOn ? ReleaseReason.Expired
                : hold.IssueIds.Count > 0 && verdict.Light == Light.Green ? ReleaseReason.Fixed
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

        if (applyAutoRelease && released.Count > 0)
            holds.Save(active);

        return new CheckResult(verdicts, active, released, applyAutoRelease, data, now);
    }

    // Ends pauses whose time ran out, without needing Microsoft's list. Used when a full check can't run,
    // because a pause must end on time no matter what. Returns the pauses it ended.
    public async Task<IReadOnlyList<Hold>> ReleaseExpiredAsync(CancellationToken ct)
    {
        var all = holds.Load();
        var now = time.GetUtcNow();
        var expired = all.Where(h => now >= h.ExpiresOn).ToList();
        if (expired.Count == 0) return [];

        var pending = await updates.GetPendingAsync(ct);
        foreach (var hold in expired.Where(h => pending.Any(u => u.UpdateId == h.UpdateId)))
            await updates.SetHiddenAsync(hold.UpdateId, false, ct);
        holds.Save([.. all.Except(expired)]);
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

    public async Task<Hold> HoldAsync(string kbText, int days, string reason, CancellationToken ct)
    {
        var kb = RequireKb(kbText);
        var update = (await updates.GetPendingAsync(ct)).FirstOrDefault(u => u.Kb == kb)
            ?? throw new PatchLightException($"{kb} isn't waiting to be installed on this PC, so there is nothing to pause.");

        IReadOnlyList<string> issueIds;
        try
        {
            var data = await issues.GetAsync(windowsVersion, ct);
            issueIds = VerdictEngine.Judge(update, data.Issues, Today(), CountBusinessIssues).Issues.Select(i => i.Id).ToList();
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
        var kb = RequireKb(kbText);
        var all = holds.Load();
        var hold = all.FirstOrDefault(h => h.Kb == kb)
            ?? throw new PatchLightException($"{kb} isn't paused by PatchLight.");

        if ((await updates.GetPendingAsync(ct)).Any(u => u.UpdateId == hold.UpdateId))
            await updates.SetHiddenAsync(hold.UpdateId, false, ct);

        holds.Save([.. all.Where(h => h.Kb != kb)]);
        return new ReleasedHold(hold, ReleaseReason.Manual);
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

    private static string RequireKb(string text) =>
        KbNumber.Normalize(text)
        ?? throw new PatchLightException($"'{text}' isn't an update number. Use one like KB5127648.");
}
