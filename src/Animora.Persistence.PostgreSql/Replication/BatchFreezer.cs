using System.Data;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Animora.Contracts.Wire.Sync;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
namespace Animora.Persistence.PostgreSql.Replication;

public sealed class BatchFreezer(IFeedContextFactory factory, IBackgroundAdmission admission,
    RecordProjector projector, FeedPageWriter writer, IOptions<FeedStorageOptions> options)
{
    private static readonly Meter Meter = new("Animora.Sync");
    private static readonly Histogram<double> Duration = Meter.CreateHistogram<double>("animora.sync.batch.build.duration", "s");
    private static readonly Histogram<double> SnapshotAge = Meter.CreateHistogram<double>("animora.pg.snapshot.age.seconds", "s");
    private static readonly Counter<long> Pages = Meter.CreateCounter<long>("animora.sync.batch.pages");
    private static readonly Counter<long> Bytes = Meter.CreateCounter<long>("animora.sync.batch.bytes");

    public static OpenBatchResponse? Decide(OpenBatchRequest request, FeedPosition current)
    {
        if (request.Epoch != current.Epoch) return new("ResyncRequired", Resync: new("EPOCH_CHANGED", current.Epoch));
        if (request.FromSequence < current.OldestRetainedSequence || request.FromSequence > current.LastSequence)
            return new("ResyncRequired", Resync: new("BEYOND_RETENTION", current.Epoch));
        if (request.FromSequence == current.LastSequence) return new("UpToDate", Current: new(current.LastSequence));
        return null;
    }

    public async Task<OpenBatchResponse> OpenAsync(OpenBatchRequest request, bool full, CancellationToken ct)
    {
        await using var lease = await admission.EnterAsync(ct).ConfigureAwait(false);
        for (int attempt = 0; ; attempt++)
        {
            try { return await Freeze(request, full, ct).ConfigureAwait(false); }
            catch (SnapshotLifetimeExceededException) when (attempt == 0 && !ct.IsCancellationRequested) { }
        }
    }

    private async Task<OpenBatchResponse> Freeze(OpenBatchRequest request, bool full, CancellationToken ct)
    {
        var watch = Stopwatch.StartNew();
        var workspace = writer.CreateWorkspace();
        try
        {
            FeedPosition position; string tenant; string id;
            var spool = Path.Combine(workspace, "snapshot.ndjson");
            await using (var db = factory.Create())
            {
                tenant = db.CurrentTenantId;
                using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
                budget.CancelAfter(options.Value.MaxSnapshotLifetime <= TimeSpan.FromSeconds(10)
                    ? options.Value.MaxSnapshotLifetime : TimeSpan.FromSeconds(10));
                try
                {
                    await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, budget.Token).ConfigureAwait(false);
                    await db.Database.ExecuteSqlRawAsync("SET LOCAL application_name = 'Animora.Server/feed'", budget.Token).ConfigureAwait(false);
                    await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", budget.Token).ConfigureAwait(false);
                    await db.Database.ExecuteSqlRawAsync("SET LOCAL TIME ZONE 'UTC'", budget.Token).ConfigureAwait(false);
                    position = await new FeedPositionReader(db).GetAsync(tenant, budget.Token).ConfigureAwait(false);
                    if (!full && Decide(request, position) is { } decision) return decision;
                    id = BatchId(tenant, position, full ? 0 : request.FromSequence, full);
                    var existing = await db.Set<FeedBatchRow>().SingleOrDefaultAsync(x => x.BatchId == id, budget.Token).ConfigureAwait(false);
                    if (existing is { State: FeedBatchState.Ready }) return Ready(existing);
                    if (existing is not null) return new("ResyncRequired", Resync: new("BEYOND_RETENTION", position.Epoch));
                    await using (var output = new StreamWriter(spool, false, new UTF8Encoding(false)))
                        await foreach (var record in projector.ReadAsync(db, position, full ? 0 : request.FromSequence, full, budget.Token).ConfigureAwait(false))
                            await output.WriteLineAsync(JsonSerializer.Serialize(record, SyncSerializerV2.Options).AsMemory(), budget.Token).ConfigureAwait(false);
                    await tx.CommitAsync(budget.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new SnapshotLifetimeExceededException(); }
            }
            SnapshotAge.Record(watch.Elapsed.TotalSeconds);
            // The snapshot connection/transaction is disposed before final page materialization.
            var pages = await writer.WriteAsync(tenant, id, workspace, FeedPageWriter.ReadSpool(spool, ct), ct).ConfigureAwait(false);
            var batch = new FeedBatchRow { TenantId = tenant, BatchId = id, Epoch = position.Epoch, Generation = position.Generation,
                FromSequenceExclusive = full ? 0 : request.FromSequence, Watermark = position.LastSequence, PageCount = pages.Count,
                ContentHash = pages.ContentHash,
                State = FeedBatchState.Ready, IsFullSnapshot = full, CreatedUtc = DateTimeOffset.UtcNow };
            await using var write = factory.Create();
            await using var publish = await write.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            // Fence epoch changes under the same feed-state lock used by epoch advance/commit sequencing.
            var state = await write.Set<TenantFeedStateRow>().FromSqlInterpolated($"SELECT * FROM public.\"TenantFeedState\" WHERE \"TenantId\"={tenant} FOR UPDATE")
                .SingleAsync(ct).ConfigureAwait(false);
            if (state.Epoch != position.Epoch || state.Generation != position.Generation)
                return new("ResyncRequired", Resync: new("EPOCH_CHANGED", state.Epoch));
            var already = await write.Set<FeedBatchRow>().SingleOrDefaultAsync(x => x.BatchId == id, ct).ConfigureAwait(false);
            if (already is not null) return already.State == FeedBatchState.Ready ? Ready(already)
                : new("ResyncRequired", Resync: new("BEYOND_RETENTION", position.Epoch));
            int buffered = 0;
            await foreach (var page in pages.ReadMetadata(ct).ConfigureAwait(false))
            {
                write.Set<FeedBatchPageRow>().Add(page);
                if (++buffered == 32)
                {
                    await write.SaveChangesAsync(ct).ConfigureAwait(false);
                    write.ChangeTracker.Clear(); buffered = 0;
                }
            }
            await write.SaveChangesAsync(ct).ConfigureAwait(false);
            write.ChangeTracker.Clear();
            write.Set<FeedBatchRow>().Add(batch); // Ready becomes visible last and together with all metadata.
            await write.SaveChangesAsync(ct).ConfigureAwait(false);
            await publish.CommitAsync(ct).ConfigureAwait(false);
            Duration.Record(watch.Elapsed.TotalSeconds); Pages.Add(pages.Count); Bytes.Add(pages.SizeBytes);
            return Ready(batch);
        }
        finally { if (Directory.Exists(workspace)) Directory.Delete(workspace, true); }
    }

    public static OpenBatchResponse Ready(FeedBatchRow b) => new("BatchReady", new(b.BatchId, b.Epoch, b.Generation,
        b.FromSequenceExclusive, b.Watermark, b.PageCount, b.ContentHash));
    public static string BatchId(string tenant, FeedPosition p, long from, bool full)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"v2|{tenant}|{p.Epoch}|{p.Generation}|{from}|{p.LastSequence}|{full}"));
        const string alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
        var value = new System.Numerics.BigInteger(bytes.AsSpan(0, 16), isUnsigned: true, isBigEndian: true);
        var result = new char[26];
        for (int i = result.Length - 1; i >= 0; i--) { result[i] = alphabet[(int)(value & 31)]; value >>= 5; }
        return new string(result);
    }
}
