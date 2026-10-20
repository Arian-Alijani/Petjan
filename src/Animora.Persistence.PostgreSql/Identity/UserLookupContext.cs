using Animora.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.PostgreSql.Identity;

/// <summary>
/// The minimal, non-sensitive projection of a user needed to bind a request to its tenant (S05).
/// Carries no password hash and no lockout state.
/// </summary>
/// <param name="id">User ULID.</param>
/// <param name="tenantId">Owning tenant ULID — the <b>stored</b> binding, never the token claim.</param>
/// <param name="securityStamp">Current stored security stamp.</param>
/// <param name="status">Account status.</param>
public sealed class UserLookupEntry(string id, string tenantId, string securityStamp, UserStatus status)
{
    /// <summary>User ULID.</summary>
    public string Id { get; } = id;

    /// <summary>Owning tenant ULID.</summary>
    public string TenantId { get; } = tenantId;

    /// <summary>Current stored security stamp.</summary>
    public string SecurityStamp { get; } = securityStamp;

    /// <summary>Account status.</summary>
    public UserStatus Status { get; } = status;
}

/// <summary>
/// Resolves a user from its <b>primary key only</b>, before any tenant is bound (S05; ISO-01 — see <see cref="UserLookupContext"/>). Like
/// <see cref="TenantDirectoryContext"/> it is a separate keyless, never-migrated context rather than an
/// <c>IgnoreQueryFilters()</c> call on the main context (banned by the S02 interceptor). It can only ever
/// return the single row addressed by id — there is no list/enumerate surface — so a caller cannot probe
/// across tenants.
/// </summary>
public interface IUserLookup
{
    /// <summary>
    /// Finds the user by ULID id; <see langword="null"/> when absent. ISO-01: the ONLY lookup key is the unguessable ULID id; the
    /// result's <c>TenantId</c> is for binding the tenant context only.
    /// </summary>
    Task<UserLookupEntry?> FindByIdAsync(string userId, CancellationToken ct = default);
}

/// <summary>
/// Keyless read context over <c>public."Users"</c> with no tenant query filter.
/// <para><b>ISO-01 invariant — read before touching this type.</b> This is the ONE deliberately tenant-filter-free read in the
/// server: it exists so token validation can resolve a user's stored tenant BEFORE any tenant is bound. It is keyless, tenant-filter-free
/// <i>by design</i> and therefore MUST be queried only by unguessable ULID primary key (<see cref="IUserLookup.FindByIdAsync"/>), and the
/// returned <see cref="UserLookupEntry.TenantId"/> may be used only to <c>Bind</c> the request's tenant context — never returned to, nor
/// selected by, untrusted caller input. Do NOT add a by-name / by-email / list / search member here or expose the entry to a caller:
/// any widening becomes a cross-tenant disclosure. The contract surface is intentionally a single by-id method.</para>
/// </summary>
public sealed class UserLookupContext(DbContextOptions<UserLookupContext> options) : DbContext(options)
{
    /// <summary>Keyless projection of users.</summary>
    public DbSet<UserLookupEntry> Users => Set<UserLookupEntry>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<UserLookupEntry>(entity =>
        {
            entity.HasNoKey();
            entity.ToTable("Users", "public", t => t.ExcludeFromMigrations());
            entity.Property(e => e.Id).HasColumnName("Id");
            entity.Property(e => e.TenantId).HasColumnName("TenantId");
            entity.Property(e => e.SecurityStamp).HasColumnName("SecurityStamp");
            entity.Property(e => e.Status).HasColumnName("Status").HasConversion<int>();
        });
    }
}

/// <inheritdoc cref="IUserLookup" />
public sealed class UserLookup(UserLookupContext context) : IUserLookup
{
    /// <inheritdoc />
    public async Task<UserLookupEntry?> FindByIdAsync(string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return null;
        }

        return await context.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
    }
}
