using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Domain.Subscriptions;
using Animora.Persistence.PostgreSql.Operator;
using Microsoft.EntityFrameworkCore;
namespace Animora.Persistence.PostgreSql.Files;
public sealed record FileMismatchRow(string TenantId, string? TenantCode, string Id, long SizeBytes, string? ShaPrefix, DateTimeOffset UpdatedUtc);
/// <summary>Vendor-only metadata operations. State change and allow-listed audit commit atomically.</summary>
public sealed class FileMismatchService(AnimoraOperatorDbContext db, TimeProvider time)
{
    public async Task<OperatorPage<FileMismatchRow>> ListAsync(int page, int size, CancellationToken ct = default)
    {
        page = Math.Clamp(page, 1, int.MaxValue / 200); size = Math.Clamp(size, 1, 200);
        var query = db.FileObjects.AsNoTracking().Where(x => x.State == FileState.Mismatch);
        long total = await query.LongCountAsync(ct).ConfigureAwait(false);
        var rows = await query.OrderByDescending(x => x.UpdatedUtc).ThenBy(x => x.TenantId).ThenBy(x => x.Id)
            .Skip((page - 1) * size).Take(size)
            .Select(x => new FileMismatchRow(x.TenantId, db.Tenants.Where(t => t.Id == x.TenantId).Select(t => t.Code).FirstOrDefault(),
                x.Id, x.SizeBytes, x.Sha256 == null ? null : x.Sha256.Substring(0, Math.Min(8, x.Sha256.Length)), x.UpdatedUtc))
            .ToListAsync(ct).ConfigureAwait(false);
        return new(rows, page, size, total);
    }
    public Task<Result> RequeueAsync(string tenantId, string fileId, ActorRef actor, CancellationToken ct = default)
        => ChangeAsync(tenantId, fileId, actor, FileState.Uploaded, "files.mismatch.requeue", ct);
    public Task<Result> AbandonAsync(string tenantId, string fileId, ActorRef actor, CancellationToken ct = default)
        => ChangeAsync(tenantId, fileId, actor, FileState.Abandoned, "files.mismatch.abandon", ct);
    private async Task<Result> ChangeAsync(string tenantId, string fileId, ActorRef actor, FileState target, string action, CancellationToken ct)
    {
        if (actor.Kind != ActorKind.Operator || string.IsNullOrWhiteSpace(actor.ActorId)) return Result.Fail(ErrorCodes.Forbidden);
        if (string.IsNullOrWhiteSpace(tenantId) || tenantId.Length > 26 || string.IsNullOrWhiteSpace(fileId) || fileId.Length > 26)
            return Result.Fail(ErrorCodes.ValidationFailed);
        var allowed = FileLifecycle.EnsureAllowed(FileState.Mismatch, target);
        if (allowed.IsFailure) return allowed;
        var now = time.GetUtcNow();
        await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        // The state predicate fences concurrent operators/reconciliation workers: only one can succeed.
        int changed = await db.FileObjects.Where(x => x.TenantId == tenantId && x.Id == fileId && x.State == FileState.Mismatch)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.State, target).SetProperty(x => x.UpdatedUtc, now), ct).ConfigureAwait(false);
        if (changed != 1)
        {
            bool exists = await db.FileObjects.AsNoTracking().AnyAsync(x => x.TenantId == tenantId && x.Id == fileId, ct).ConfigureAwait(false);
            return Result.Fail(exists ? ErrorCodes.FileHashMismatch : ErrorCodes.FileNotFound);
        }
        db.Audit.Add(OperatorAudit.Create(now, actor, action, tenantId, IdGenerator.NewId(now), detail: new { fileId }));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);
        return Result.Ok();
    }
}
