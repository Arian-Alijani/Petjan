namespace Animora.Persistence.PostgreSql.Tenancy;

/// <summary>
/// Ambient tenant of the current unit of work. Bound per request by S05; S02 ships
/// <see cref="NullTenantContext"/> as the safe default (no tenant ⇒ queries throw).
/// </summary>
public interface ITenantContext
{
    /// <summary>The tenant every filtered query/write is scoped to; <c>null</c> when none is bound.</summary>
    string? TenantId { get; }

    /// <summary>
    /// <c>true</c> for platform operators. Operators may write <c>Tenant</c> rows only
    /// and never bypass the tenant filter implicitly.
    /// </summary>
    bool IsOperator { get; }
}
