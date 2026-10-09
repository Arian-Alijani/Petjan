using System.Data.Common;

namespace Animora.Persistence.PostgreSql.Tenancy;

/// <summary>
/// Hook invoked once per database transaction (and again on every serialization/deadlock retry)
/// so a future PostgreSQL Row-Level-Security policy can bind the ambient tenant to the session
/// before any statement runs. S03 ships <see cref="NoopTenantSessionInitializer"/> as the default
/// (RLS <b>off</b>): isolation is proven by the three application/data-access layers with hardening
/// off (ARCHITECTURE.md §9.2, gate L), and this seam exists only so enabling RLS later adds no new
/// transaction plumbing.
/// </summary>
/// <remarks>
/// The eventual (not built here) <c>RlsTenantSessionInitializer</c> contract is binding:
/// it must execute <c>SELECT set_config('animora.tenant_id', @t, true)</c> with the tenant passed as a
/// <b>parameter</b> (never string-interpolated), on <b>every</b> transaction begin and <b>every</b> retry,
/// so the RLS policy can read <c>current_setting('animora.tenant_id')</c>. Enabling RLS is gated on G07
/// (ROADMAP §5 deferred); until then the Noop implementation is the only one registered.
/// </remarks>
public interface ITenantSessionInitializer
{
    /// <summary>
    /// Binds <paramref name="tenantId"/> to the current session on <paramref name="connection"/>.
    /// Called inside the open transaction, before the unit of work runs its first statement, and again
    /// on each retry. The Noop default performs no I/O and must not touch the connection.
    /// </summary>
    /// <param name="connection">The open connection enlisted in the current transaction.</param>
    /// <param name="tenantId">The ambient tenant id (empty string when none is bound).</param>
    /// <param name="ct">Cancellation token.</param>
    ValueTask InitializeAsync(DbConnection connection, string tenantId, CancellationToken ct = default);
}
