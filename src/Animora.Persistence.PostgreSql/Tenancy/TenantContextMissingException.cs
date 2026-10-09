namespace Animora.Persistence.PostgreSql.Tenancy;

/// <summary>
/// Raised when a tenant-filtered query or a tenant-stamped write runs without a tenant
/// in context. Failing loudly prevents a null tenant from silently matching nothing/everything.
/// </summary>
public sealed class TenantContextMissingException : Exception
{
    /// <summary>Creates the exception with the standard message.</summary>
    public TenantContextMissingException()
        : base("No tenant in context for a tenant-filtered query.")
    {
    }

    /// <summary>Creates the exception with a custom message.</summary>
    public TenantContextMissingException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a custom message and inner exception.</summary>
    public TenantContextMissingException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
