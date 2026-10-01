using Animora.Domain.Primitives;

namespace Animora.Persistence.Sqlite.Entities;

/// <summary>
/// Tenant-scoped, monotonically increasing "source generation" of everything the dashboard reads. It is
/// advanced by database triggers (migration 021) in the very same transaction as each source write — OLTP rows,
/// derived facts written later by the projection worker, and projection rebuilds alike — so a rollback never
/// advances it and a late projection write always does. The dashboard cache uses it as its version, replacing
/// the "highest activity-event Id" probe that missed every write without a new feed row. Never replicated and
/// never written by application code.
/// </summary>
[LocalOnly]
public sealed class DashboardGeneration
{
    private DashboardGeneration() { }

    /// <summary>Owning tenant; also the primary key (exactly one row per tenant).</summary>
    public string TenantId { get; private set; } = string.Empty;

    /// <summary>Number of dashboard-relevant source writes ever committed for the tenant.</summary>
    public long Value { get; private set; }
}
