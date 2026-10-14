using System.Runtime.InteropServices;
using Microsoft.CSharp.RuntimeBinder;
using System.Runtime.Versioning;
using PatchLight.Core;

namespace PatchLight.Windows;

// Late-bound Windows Update Agent API (Microsoft.Update.Session).
[SupportedOSPlatform("windows")]
public sealed class WindowsUpdateClient : IUpdateClient
{
    private const string PendingCriteria = "IsInstalled=0 and IsHidden=0 or IsInstalled=0 and IsHidden=1";

    // Upgrades to a new Windows version ("Windows 11, version 26H2") are optional offers, missed by the search above.
    private const string OptionalCriteria =
        "IsInstalled=0 and IsHidden=0 and DeploymentAction='OptionalInstallation' or " +
        "IsInstalled=0 and IsHidden=1 and DeploymentAction='OptionalInstallation'";
    private const string UpgradesCategory = "3689bdc8-b205-4af4-8d4a-a63924c5e9d5";
    private const int OperationInstall = 1;
    private const int ResultSucceeded = 2;
    private const int ResultSucceededWithErrors = 3;

    public Task<IReadOnlyList<PendingUpdate>> GetPendingAsync(CancellationToken ct) => Guard(() =>
    {
        var list = new List<PendingUpdate>();
        foreach (var (update, isUpgrade) in Offered(ct))
        {
            var pending = ToPending(update, isUpgrade);
            if (pending is not null && list.All(p => p.UpdateId != pending.UpdateId)) list.Add(pending);
        }
        return (IReadOnlyList<PendingUpdate>)list;
    }, ct);

    public Task SetHiddenAsync(string updateId, bool hidden, CancellationToken ct) => Guard(() =>
    {
        foreach (var (update, _) in Offered(ct))
        {
            if ((string)update.Identity.UpdateID != updateId) continue;
            try
            {
                update.IsHidden = hidden;
            }
            catch (COMException ex) when ((uint)ex.HResult is 0x80240044 or 0x80070005)
            {
                throw new PatchLightException("Run PatchLight as administrator to pause or resume updates.");
            }
            return;
        }
        throw new PatchLightException("That update is no longer offered by Windows Update.");
    }, ct);

    public Task<IReadOnlyList<InstalledUpdate>> GetHistoryAsync(int max, CancellationToken ct) => Guard(() =>
    {
        dynamic searcher = NewSearcher();
        int total = searcher.GetTotalHistoryCount();
        var list = new List<InstalledUpdate>();
        if (total == 0) return (IReadOnlyList<InstalledUpdate>)list;

        dynamic entries = searcher.QueryHistory(0, Math.Min(total, max));
        for (var i = 0; i < (int)entries.Count; i++)
        {
            dynamic entry = entries[i];
            int operation = entry.Operation;
            int result = entry.ResultCode;
            if (operation != OperationInstall || (result != ResultSucceeded && result != ResultSucceededWithErrors)) continue;

            string title = entry.Title;
            DateTime date = entry.Date;
            list.Add(new InstalledUpdate(UpdateTitle.TryGetKb(title), title, UpdateTitle.TryGetBuild(title),
                new DateTimeOffset(DateTime.SpecifyKind(date, DateTimeKind.Utc))));
        }
        return (IReadOnlyList<InstalledUpdate>)list;
    }, ct);

    // Every update Windows offers: the regular ones, plus upgrades to a new Windows version.
    private static IEnumerable<(dynamic Update, bool IsUpgrade)> Offered(CancellationToken ct)
    {
        dynamic searcher = NewSearcher();
        dynamic regular = searcher.Search(PendingCriteria).Updates;
        for (var i = 0; i < (int)regular.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            dynamic update = regular[i];
            // Near the end of a version's support Windows can offer the upgrade as a regular update.
            yield return (update, IsUpgrade(update));
        }

        foreach (var update in OptionalUpgrades(searcher, ct))
            yield return (update, true);
    }

    // Upgrades offered as optional. Extra information: if this search fails, the regular updates still count.
    private static List<dynamic> OptionalUpgrades(dynamic searcher, CancellationToken ct)
    {
        var upgrades = new List<dynamic>();
        try
        {
            dynamic optional = searcher.Search(OptionalCriteria).Updates;
            for (var i = 0; i < (int)optional.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                dynamic update = optional[i];
                if (IsUpgrade(update)) upgrades.Add(update);
            }
        }
        catch (Exception ex) when (ex is COMException or RuntimeBinderException)
        {
        }
        return upgrades;
    }

    private static bool IsUpgrade(dynamic update)
    {
        dynamic categories = update.Categories;
        for (var i = 0; i < (int)categories.Count; i++)
        {
            if (string.Equals((string)categories[i].CategoryID, UpgradesCategory, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static PendingUpdate? ToPending(dynamic update, bool isUpgrade)
    {
        string title = update.Title;
        if (isUpgrade)
        {
            // Windows gives the upgrade the same KB as that month's security update, so it's known by its version.
            var version = UpdateTitle.VersionIn(title);
            return version is null ? null
                : new PendingUpdate((string)update.Identity.UpdateID, null, title, null, null, (bool)update.IsHidden, version);
        }
        string? kb = null;
        dynamic kbs = update.KBArticleIDs;
        if ((int)kbs.Count > 0) kb = KbNumber.Normalize((string)kbs[0]);
        DateTime deployed = update.LastDeploymentChangeTime;
        return new PendingUpdate(
            (string)update.Identity.UpdateID,
            kb ?? UpdateTitle.TryGetKb(title),
            title,
            UpdateTitle.TryGetBuild(title),
            deployed.Year > 2000 ? DateOnly.FromDateTime(deployed) : null,
            (bool)update.IsHidden);
    }

    // COM and late-binding failures become one plain message instead of a stack trace.
    private static Task<T> Guard<T>(Func<T> body, CancellationToken ct) => Task.Run(() =>
    {
        try
        {
            return body();
        }
        catch (Exception ex) when (ex is COMException or RuntimeBinderException)
        {
            throw new PatchLightException(
                "Couldn't talk to Windows Update. Check your internet connection and that the Windows Update service is running.");
        }
    }, ct);

    private static Task Guard(Action body, CancellationToken ct) => Guard(() => { body(); return 0; }, ct);

    private static dynamic NewSearcher()
    {
        var type = Type.GetTypeFromProgID("Microsoft.Update.Session")
            ?? throw new PatchLightException("Windows Update isn't available on this PC.");
        dynamic session = Activator.CreateInstance(type)!;
        session.ClientApplicationID = "PatchLight";
        return session.CreateUpdateSearcher();
    }
}
