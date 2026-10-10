namespace Animora.Persistence.PostgreSql.Replication;

public sealed class TenantFeedStateRow
{
    public string TenantId { get; set; } = string.Empty;
    public long Epoch { get; set; } = 1;
    public long Generation { get; set; } = 1;
    public long LastSequence { get; set; }
    public long OldestRetainedSequence { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
}
