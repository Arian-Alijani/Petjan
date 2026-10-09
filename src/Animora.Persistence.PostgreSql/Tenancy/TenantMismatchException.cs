namespace Animora.Persistence.PostgreSql.Tenancy;

/// <summary>
/// Raised when a write targets a tenant other than the one in context. The caller maps
/// it to <c>ErrorCodes.TenantMismatch</c> and records an audit entry from the carried ids.
/// </summary>
public sealed class TenantMismatchException : Exception
{
    /// <summary>Creates the exception for the attempted vs. context tenant pair.</summary>
    public TenantMismatchException(string? contextTenant, string entityTenant)
        : base("Entity tenant does not match the tenant in context.")
    {
        ContextTenant = contextTenant;
        EntityTenant = entityTenant;
    }

    /// <summary>Creates the exception with a custom message.</summary>
    public TenantMismatchException(string message)
        : base(message)
    {
        EntityTenant = string.Empty;
    }

    /// <summary>Creates the exception with a custom message and inner exception.</summary>
    public TenantMismatchException(string message, Exception innerException)
        : base(message, innerException)
    {
        EntityTenant = string.Empty;
    }

    /// <summary>The tenant bound to the current context (may be <c>null</c>).</summary>
    public string? ContextTenant { get; }

    /// <summary>The tenant carried by the offending entity.</summary>
    public string EntityTenant { get; }
}
