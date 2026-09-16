using System.Threading.Tasks;
using Animora.Contracts.Navigation;

namespace Animora.Contracts.Dashboard;

/// <summary>
/// Data seam for the Dashboard screen (Phase 06). The mock implementation
/// lives in Animora.Desktop/Mocks; a future backend phase swaps in a real
/// Datasync-backed implementation with no ViewModel/View changes.
/// </summary>
public interface IDashboardQueryService
{
    /// <summary>Loads the role-specific dashboard snapshot for <paramref name="role"/>.</summary>
    Task<DashboardSnapshot> GetSnapshotAsync(UserRole role);
}
