namespace Animora.Persistence.PostgreSql.Replication;

public enum FeedBatchState : short { Building, Ready, Retired }
public sealed class FeedBatchRow
{
    public string TenantId { get; set; } = string.Empty;
    public string BatchId { get; set; } = string.Empty;
    public long Epoch { get; set; }
    public long Generation { get; set; }
    public long FromSequenceExclusive { get; set; }
    public long Watermark { get; set; }
    public int PageCount { get; set; }
    public string ContentHash { get; set; } = string.Empty;
    public FeedBatchState State { get; set; }
    public bool IsFullSnapshot { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset? RetiredUtc { get; set; }
}
