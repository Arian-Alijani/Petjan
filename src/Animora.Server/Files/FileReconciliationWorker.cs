using System.Diagnostics.Metrics;
using Animora.Domain.Primitives;
using Animora.Persistence.PostgreSql.Context;
using Animora.Persistence.PostgreSql.Files;
using Animora.Persistence.PostgreSql.Identity;
using Animora.Persistence.PostgreSql.Outbox;
using Animora.Persistence.PostgreSql.Replication;
using Animora.Server.Workers;
using Microsoft.EntityFrameworkCore;
namespace Animora.Server.Files;

internal sealed class FileReconciliationWorker : ResilientWorker
{
    private readonly IServiceScopeFactory _scopes; private readonly IBackgroundAdmission _admission;
    private readonly FileMaintenanceStore _maintenance; private readonly FileStore _store;
    private readonly Animora.Server.Observability.ServerMetrics _metrics;
    private IEnumerator<string>? _orphans;
    public FileReconciliationWorker(IServiceScopeFactory scopes, IBackgroundAdmission admission, FileMaintenanceStore maintenance,
        FileStore store, TimeProvider clock, WorkerHeartbeat heartbeat, ILogger<FileReconciliationWorker> logger, Animora.Server.Observability.ServerMetrics metrics)
        : base("files-reconciliation", TimeSpan.FromHours(1), clock, heartbeat, logger)
    { _scopes = scopes; _admission = admission; _maintenance = maintenance; _store = store; _metrics = metrics; }
    internal Task RunOnceAsync(CancellationToken ct = default) => TickAsync(ct);
    protected override async Task TickAsync(CancellationToken ct)
    {
        await using var admission = await _admission.EnterAsync(ct).ConfigureAwait(false);
        foreach (bool sample in new[] { false, true })
        foreach (var candidate in await _maintenance.CandidatesAsync(sample, ct).ConfigureAwait(false))
        {
            await using var scope = _scopes.CreateAsyncScope(); var sp = scope.ServiceProvider;
            sp.GetRequiredService<WorkerExecutionContext>().IsWorker = true;
            sp.GetRequiredService<AuthTenantContext>().Bind(candidate.TenantId);
            var db = sp.GetRequiredService<AnimoraServerDbContext>();
            await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            var row = await db.FileObjects.SingleOrDefaultAsync(x => x.Id == candidate.Id, ct).ConfigureAwait(false);
            if (row is null) continue;
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"file-upload:" + row.TenantId + ":" + row.UploadId},0))", ct).ConfigureAwait(false);
            await db.Entry(row).ReloadAsync(ct).ConfigureAwait(false);
            var now = Clock.GetUtcNow(); string? finding = null;
            if (!sample && row.State is FileState.Staged or FileState.Uploaded && now - row.UpdatedUtc > TimeSpan.FromHours(24))
            { row.State = FileState.Abandoned; finding = "abandoned"; }
            if (row.State == FileState.Abandoned && row.UploadId is { } upload) File.Delete(_store.Staging(row.TenantId, upload));
            if (row.State is FileState.Committed or FileState.Available && row.Sha256 is { } hash)
            {
                string blob = _store.Blob(hash);
                if (!File.Exists(blob)) finding = "blob_missing";
                else if (sample && await _store.HashAsync(blob, ct).ConfigureAwait(false) != hash) finding = "bitrot";
                if (finding is "blob_missing" or "bitrot") row.State = FileState.Mismatch;
            }
            if (finding is not null)
            {
                row.UpdatedUtc = now;
                db.SecurityAudit.Add(SecurityAuditEntry.Create(IdGenerator.NewId(now), row.TenantId, now, "files",
                    "files.reconcile." + finding, "success", IdGenerator.NewId(now), null, null, SecurityAuditDetail.Create(("fileId", row.Id))));
                _metrics.Reconcile(finding);
            }
            await db.SaveChangesAsync(ct).ConfigureAwait(false); await tx.CommitAsync(ct).ConfigureAwait(false);
        }
        foreach (string hash in await _maintenance.GarbageAsync(Clock.GetUtcNow() - TimeSpan.FromHours(24), ct).ConfigureAwait(false))
            if (await _maintenance.CollectAsync(hash, Clock.GetUtcNow() - TimeSpan.FromHours(24), () => { File.Delete(_store.Blob(hash)); return Task.CompletedTask; }, false, ct).ConfigureAwait(false))
                Finding("zero_ref");
        // Bounded FS scan, cursor retained across passes instead of repeatedly scanning the first directory.
        if (!Directory.Exists(_store.Root)) return;
        _orphans ??= Directory.EnumerateFiles(_store.Root,"*",SearchOption.AllDirectories).GetEnumerator();
        // 100 metadata candidates + 20 integrity samples + 20 GC candidates + 60 orphan checks = 200.
        for (int i = 0; i < 60; i++)
        {
            if (!_orphans.MoveNext()) { _orphans.Dispose(); _orphans = null; break; }
            string path = _orphans.Current; string name = Path.GetFileName(path);
            if (!File.Exists(path) || Clock.GetUtcNow().UtcDateTime - File.GetLastWriteTimeUtc(path) < TimeSpan.FromHours(1)) continue;
            var relative=Path.GetRelativePath(_store.Root,path).Split(Path.DirectorySeparatorChar);
            if(relative.Length==3&&relative[0]=="staging"&&name.EndsWith(".part",StringComparison.Ordinal))
            {
                var tenant=relative[1];var upload=name[..^5];
                // Validate components through the store before obtaining the shared upload fence.
                try { if(_store.Staging(tenant,upload)!=path)continue; } catch(ArgumentException) {continue;}
                if(await _maintenance.CollectStagingAsync(tenant,upload,()=>{File.Delete(path);return Task.CompletedTask;},ct).ConfigureAwait(false))Finding("orphan");
            }
            else if(relative.Length==4&&relative[0]=="cas"&&FileStore.IsHash(name))
            {
                if(await _maintenance.CollectAsync(name,Clock.GetUtcNow(),()=>{File.Delete(path);return Task.CompletedTask;},true,ct).ConfigureAwait(false))Finding("orphan");
            }
            else if (_store.TryPublisherTemporary(path, out string temporaryHash))
            {
                var before = Clock.GetUtcNow() - TimeSpan.FromHours(1);
                if (await _maintenance.CollectPublisherTemporaryAsync(temporaryHash,
                    () => _store.DeletePublisherTemporary(path, before), ct).ConfigureAwait(false)) Finding("orphan");
            }
            // Unknown/operator files and manifests are never deleted by guessed filenames.
        }
    }
    private void Finding(string name) { _metrics.Reconcile(name); Logger.LogInformation("File reconciliation {Finding}", name); }
    public override void Dispose() { _orphans?.Dispose(); base.Dispose(); }
}
