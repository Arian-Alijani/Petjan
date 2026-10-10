namespace Animora.Persistence.PostgreSql.Context;

/// <summary>Transaction-owned, tenant-scoped document numbering; no client counters are trusted.</summary>
public sealed class DocumentCounter
{
    public string TenantId { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string Period { get; set; } = string.Empty;
    public long Next { get; set; }
}
