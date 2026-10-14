namespace PatchLight.Core;

public interface IUpdateClient
{
    // Includes updates PatchLight (or the user) has hidden.
    Task<IReadOnlyList<PendingUpdate>> GetPendingAsync(CancellationToken ct);

    // Needs administrator rights; throws PatchLightException with a plain message otherwise.
    Task SetHiddenAsync(string updateId, bool hidden, CancellationToken ct);

    Task<IReadOnlyList<InstalledUpdate>> GetHistoryAsync(int max, CancellationToken ct);
}
