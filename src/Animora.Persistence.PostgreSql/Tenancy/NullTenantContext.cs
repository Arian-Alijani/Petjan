namespace Animora.Persistence.PostgreSql.Tenancy;

/// <summary>Default <see cref="ITenantContext"/>: no tenant, not an operator.</summary>
public sealed class NullTenantContext : ITenantContext
{
    /// <inheritdoc />
    public string? TenantId => null;

    /// <inheritdoc />
    public bool IsOperator => false;
}
