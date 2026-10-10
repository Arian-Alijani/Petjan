namespace Animora.Persistence.PostgreSql.Replication;

public sealed class FeedBatchPageRow
{
    public string TenantId { get; set; } = string.Empty;
    public string BatchId { get; set; } = string.Empty;
    public int PageOrdinal { get; set; }
    public string PageHash { get; set; } = string.Empty;
    public int SizeBytes { get; set; }
}
