using Animora.Domain.Identity;
using Animora.Domain.Results;

namespace Animora.Persistence.PostgreSql.Identity;

/// <summary>
/// Resolves a tenant from its human-entered sign-in code. This is the <b>only</b> unfiltered read of
/// <c>Tenants</c>: the tenant has to be known before any tenant-filtered query can run.
/// </summary>
public interface ITenantDirectory
{
    /// <summary>
    /// Finds a tenant by code (case-insensitive). A miss returns <c>CredentialsInvalid</c> so the caller
    /// cannot enumerate tenants.
    /// </summary>
    Task<Result<TenantDirectoryEntry>> FindByCodeAsync(string code, CancellationToken ct = default);

    /// <summary>
    /// Finds a tenant by id (used to re-check the lifecycle status on refresh). A miss returns
    /// <c>CredentialsInvalid</c>.
    /// </summary>
    Task<Result<TenantDirectoryEntry>> FindByIdAsync(string tenantId, CancellationToken ct = default);
}

/// <summary>The minimal, non-sensitive projection of a tenant that sign-in needs.</summary>
public sealed class TenantDirectoryEntry(string id, string? code, string displayName, TenantStatus status)
{
    /// <summary>Tenant ULID.</summary>
    public string Id { get; } = id;

    /// <summary>Normalized sign-in code.</summary>
    public string? Code { get; } = code;

    /// <summary>Display name.</summary>
    public string DisplayName { get; } = displayName;

    /// <summary>Lifecycle status.</summary>
    public TenantStatus Status { get; } = status;
}
