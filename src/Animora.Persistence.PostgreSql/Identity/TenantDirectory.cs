using Animora.Domain.Results;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.PostgreSql.Identity;

/// <summary>
/// Read-only keyless view over <c>public."Tenants"</c> with <b>no</b> tenant query filter. It is a
/// separate context (never <c>IgnoreQueryFilters()</c> — the S02 interceptor bans it) and is never
/// part of migrations: the table is owned by <c>AnimoraServerDbContext</c>.
/// </summary>
public sealed class TenantDirectoryContext(DbContextOptions<TenantDirectoryContext> options) : DbContext(options)
{
    /// <summary>Keyless projection of tenants.</summary>
    public DbSet<TenantDirectoryEntry> Tenants => Set<TenantDirectoryEntry>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<TenantDirectoryEntry>(entity =>
        {
            entity.HasNoKey();
            entity.ToTable("Tenants", "public", t => t.ExcludeFromMigrations());
            entity.Property(e => e.Id).HasColumnName("Id");
            entity.Property(e => e.Code).HasColumnName("Code");
            entity.Property(e => e.DisplayName).HasColumnName("DisplayName");
            entity.Property(e => e.Status).HasColumnName("Status").HasConversion<int>();
        });
    }
}

/// <inheritdoc cref="ITenantDirectory" />
public sealed class TenantDirectory(TenantDirectoryContext context) : ITenantDirectory
{
    /// <summary>Normalizes a user-entered tenant code (trim + lowercase invariant).</summary>
    public static string NormalizeCode(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        return code.Trim().ToLowerInvariant();
    }

    /// <inheritdoc />
    public async Task<Result<TenantDirectoryEntry>> FindByCodeAsync(string code, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return Result<TenantDirectoryEntry>.Fail(ErrorCodes.CredentialsInvalid);
        }

        string normalized = NormalizeCode(code);
        TenantDirectoryEntry? entry = await context.Tenants
            .AsNoTracking()
            .Where(t => t.Code == normalized)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        return entry is null
            ? Result<TenantDirectoryEntry>.Fail(ErrorCodes.CredentialsInvalid)
            : Result<TenantDirectoryEntry>.Ok(entry);
    }

    /// <inheritdoc />
    public async Task<Result<TenantDirectoryEntry>> FindByIdAsync(string tenantId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return Result<TenantDirectoryEntry>.Fail(ErrorCodes.CredentialsInvalid);
        }

        TenantDirectoryEntry? entry = await context.Tenants
            .AsNoTracking()
            .Where(t => t.Id == tenantId)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        return entry is null
            ? Result<TenantDirectoryEntry>.Fail(ErrorCodes.CredentialsInvalid)
            : Result<TenantDirectoryEntry>.Ok(entry);
    }
}
