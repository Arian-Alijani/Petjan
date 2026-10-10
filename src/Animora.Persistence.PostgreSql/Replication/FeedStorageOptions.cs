namespace Animora.Persistence.PostgreSql.Replication;

public sealed class FeedStorageOptions
{
    public string DataRootPath { get; set; } = string.Empty;
    public TimeSpan MaxSnapshotLifetime { get; set; } = TimeSpan.FromSeconds(10);
    public const int PageTargetBytes = 262144;
}
public sealed class SnapshotLifetimeExceededException : Exception
{
    public SnapshotLifetimeExceededException() : base("The feed snapshot exceeded its lifetime budget.") { }
}
