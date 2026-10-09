using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.PostgreSql.KeyRing;

/// <summary>
/// Dedicated context persisting the ASP.NET Core DataProtection key ring. Kept separate from
/// <c>AnimoraServerDbContext</c> (own pool, own migration history) so key access never competes
/// with OLTP traffic and the ring survives restarts — there is no in-memory fallback.
/// </summary>
public sealed class AnimoraKeyRingDbContext(DbContextOptions<AnimoraKeyRingDbContext> options)
    : DbContext(options), IDataProtectionKeyContext
{
    /// <summary>Own migrations history table, isolated from the main context's history.</summary>
    public const string MigrationsHistoryTable = "__EFMigrationsHistory_KeyRing";

    /// <inheritdoc />
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();
}
