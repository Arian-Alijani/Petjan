using Microsoft.EntityFrameworkCore;
namespace Animora.Persistence.PostgreSql.Replication;

/// <summary>Idempotent retire → files → metadata → journal+floor → orphans, never on OLTP requests.</summary>
public sealed class FeedRetentionService(IFeedContextFactory factory, FeedPageWriter writer, IBackgroundAdmission admission)
{
    public async Task RunAsync(CancellationToken ct)
    {
        await using var lease = await admission.EnterAsync(ct).ConfigureAwait(false);
        await using var db = factory.Create();
        var now = DateTimeOffset.UtcNow;
        var expired = now - RetentionPolicy.BatchLifetime;
        var tenant = db.CurrentTenantId;
        await db.Set<FeedBatchRow>().Where(b => b.State == FeedBatchState.Ready && b.CreatedUtc < expired &&
            db.Set<FeedBatchRow>().Any(n => n.Epoch == b.Epoch && n.State == FeedBatchState.Ready &&
                (n.CreatedUtc > b.CreatedUtc || (n.CreatedUtc == b.CreatedUtc && string.Compare(n.BatchId, b.BatchId) > 0))))
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.State, FeedBatchState.Retired).SetProperty(b => b.RetiredUtc, now), ct).ConfigureAwait(false);
        // Keyset pages avoid keeping a reader open while deleting metadata on the same connection.
        string after = "";
        while (true)
        {
            var batch = await db.Set<FeedBatchRow>().Where(b => b.State == FeedBatchState.Retired && string.Compare(b.BatchId, after) > 0)
                .OrderBy(b => b.BatchId).FirstOrDefaultAsync(ct).ConfigureAwait(false);
            if (batch is null) break;
            after = batch.BatchId;
            var dir = writer.BatchDirectory(tenant, batch.BatchId);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            await using var cleanup = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            await db.Set<FeedBatchPageRow>().Where(p => p.BatchId == batch.BatchId).ExecuteDeleteAsync(ct).ConfigureAwait(false);
            await db.Set<FeedBatchRow>().Where(b => b.BatchId == batch.BatchId && b.State == FeedBatchState.Retired).ExecuteDeleteAsync(ct).ConfigureAwait(false);
            await cleanup.CommitAsync(ct).ConfigureAwait(false);
        }
        await using (var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false))
        {
            var state = await db.Set<TenantFeedStateRow>().FromSqlInterpolated($"SELECT * FROM public.\"TenantFeedState\" WHERE \"TenantId\"={tenant} FOR UPDATE")
                .SingleAsync(ct).ConfigureAwait(false);
            var cutoffTime = now.AddDays(-RetentionPolicy.JournalRetentionDays);
            // Retain the contiguous suffix starting with the first young row. CapturedUtc is
            // retention bookkeeping, NEVER a feed ordering/cursor authority.
            long? firstYoung = await db.Set<TenantJournalRow>().Where(j => j.CapturedUtc >= cutoffTime && j.ChangeSequence != null)
                .MinAsync(j => j.ChangeSequence, ct).ConfigureAwait(false);
            long floor = Math.Max(state.OldestRetainedSequence, firstYoung is { } seq ? seq - 1 : state.LastSequence);
            await db.Set<TenantJournalRow>().Where(j => j.ChangeSequence <= floor).ExecuteDeleteAsync(ct).ConfigureAwait(false);
            await db.Set<TenantFeedStateRow>().ExecuteUpdateAsync(s => s.SetProperty(f => f.OldestRetainedSequence, floor)
                .SetProperty(f => f.UpdatedUtc, now), ct).ConfigureAwait(false);
            await tx.CommitAsync(ct).ConfigureAwait(false);
        }
        var root = Path.Combine(writer.FeedRoot, tenant);
        if (Directory.Exists(root))
            foreach (var dir in Directory.EnumerateDirectories(root))
            {
                ct.ThrowIfCancellationRequested();
                var id = Path.GetFileName(dir);
                if (Directory.GetLastWriteTimeUtc(dir) > (now - RetentionPolicy.OrphanLifetime).UtcDateTime) continue;
                if (!await db.Set<FeedBatchRow>().AnyAsync(b => b.BatchId == id, ct).ConfigureAwait(false)) Directory.Delete(dir, true);
            }
        var staging = Path.Combine(writer.FeedRoot, "_staging");
        if (Directory.Exists(staging))
            foreach (var dir in Directory.EnumerateDirectories(staging))
                if (Directory.GetLastWriteTimeUtc(dir) < (now - RetentionPolicy.OrphanLifetime).UtcDateTime) Directory.Delete(dir, true);
    }
}
