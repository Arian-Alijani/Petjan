namespace Animora.Persistence.PostgreSql.Replication;

public sealed record FeedPosition(long Epoch, long Generation, long LastSequence, long OldestRetainedSequence);
public interface IFeedPosition
{
    Task<FeedPosition> GetAsync(string tenantId, CancellationToken ct = default);
}
public interface IFeedEpoch
{
    Task AdvanceAsync(string tenantId, string reason, CancellationToken ct = default);
}
