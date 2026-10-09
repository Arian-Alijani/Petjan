using Animora.Persistence.PostgreSql.Tenancy;

namespace Animora.Persistence.PostgreSql.Identity;

/// <summary>
/// Scoped, explicitly-bound <see cref="ITenantContext"/> for the S04 authentication plane. Sign-in
/// resolves the tenant first (via <see cref="ITenantDirectory"/>) and only then binds it here, so every
/// subsequent user/device/token query is tenant-filtered. S05 replaces this with the request-bound
/// tenant-context middleware; until then an unbound context fails closed
/// (<see cref="TenantContextMissingException"/>).
/// </summary>
public sealed class AuthTenantContext : ITenantContext
{
    /// <inheritdoc />
    public string? TenantId { get; private set; }

    /// <inheritdoc />
    public bool IsOperator => false;

    /// <summary>Binds the tenant for the remainder of this scope. Re-binding to another tenant is refused.</summary>
    /// <param name="tenantId">The resolved tenant ULID.</param>
    public void Bind(string tenantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        if (TenantId is not null && !string.Equals(TenantId, tenantId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The auth tenant context is already bound to a different tenant.");
        }

        TenantId = tenantId;
    }
}
