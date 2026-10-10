namespace Animora.Persistence.PostgreSql.Replication;

public sealed class TenantJournalRow
{
    public string TenantId { get; set; } = string.Empty;
    public long Id { get; set; }
    public string Entity { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public char Op { get; set; }
    public Guid? Version { get; set; }
    public long? ChangeSequence { get; set; }
    public long Epoch { get; set; }
    public DateTimeOffset CapturedUtc { get; set; }
}
