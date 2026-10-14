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
    private const int OperationInstall = 1;
    private const int ResultSucceeded = 2;
    private const int ResultSucceededWithErrors = 3;

    public Task<IReadOnlyList<PendingUpdate>> GetPendingAsync(CancellationToken ct) => Guard(() =>
    {
        dynamic updates = NewSearcher().Search(PendingCriteria).Updates;
        var list = new List<PendingUpdate>();
        for (var i = 0; i < (int)updates.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            list.Add(ToPending(updates[i]));
        }
        return (IReadOnlyList<PendingUpdate>)list;
    }, ct);

    public Task SetHiddenAsync(string updateId, bool hidden, CancellationToken ct) => Guard(() =>
    {
        dynamic updates = NewSearcher().Search(PendingCriteria).Updates;
        for (var i = 0; i < (int)updates.Count; i++)
        {
            dynamic update = updates[i];
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

    private static PendingUpdate ToPending(dynamic update)
    {
        string title = update.Title;
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
