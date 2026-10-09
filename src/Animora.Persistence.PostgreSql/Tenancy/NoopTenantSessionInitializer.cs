using System.Data.Common;

namespace Animora.Persistence.PostgreSql.Tenancy;

/// <summary>
/// Default <see cref="ITenantSessionInitializer"/>: RLS is <b>off</b>, so it performs no I/O and never
/// touches the connection. Tenant isolation is guaranteed by the application/data-access/write-stamping
/// layers (ARCHITECTURE.md §9.2) and proven with hardening off by the S03 isolation test suite. Replacing
/// this with an RLS-enabling implementation is gated on G07.
/// </summary>
public sealed class NoopTenantSessionInitializer : ITenantSessionInitializer
{
    /// <inheritdoc />
    public ValueTask InitializeAsync(DbConnection connection, string tenantId, CancellationToken ct = default) =>
        ValueTask.CompletedTask;
}
