using System.Threading;
using System.Threading.Tasks;
using Animora.Contracts.Navigation;

namespace Animora.Contracts.Dashboard;

/// <summary>
/// Data seam for the Dashboard screen. The production implementation
/// (Animora.Client.Services) reads the local SQLite store through the authorized query pipeline;
/// a future server-backed implementation can replace it with no ViewModel/View changes.
/// </summary>
public interface IDashboardQueryService
{
    /// <summary>
    /// Loads the role-specific dashboard snapshot for <paramref name="role"/>. <paramref name="ct"/> is honoured
    /// while waiting for the load gate and by every database read; a cancelled call throws
    /// <see cref="OperationCanceledException"/> (never a failure outcome) and never yields a result to apply.
    /// </summary>
    Task<DashboardSnapshot> GetSnapshotAsync(UserRole role, CancellationToken ct = default);

    /// <summary>Explicit refresh bypasses the data cache, never live authorization.</summary>
    Task<DashboardSnapshot> RefreshSnapshotAsync(UserRole role, CancellationToken ct = default) => GetSnapshotAsync(role, ct);
}
