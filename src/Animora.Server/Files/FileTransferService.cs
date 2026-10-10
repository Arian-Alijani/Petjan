using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Persistence.PostgreSql.Context;
using Animora.Persistence.PostgreSql.Files;
using Microsoft.EntityFrameworkCore;
namespace Animora.Server.Files;

internal sealed record FileDeclaration(string Name, string ContentType, long Size, string? Sha256);
internal sealed record ChunkOutcome(bool Accepted, long NextOffset, ErrorCode? Error = null);
internal sealed class FileTransferService(AnimoraServerDbContext db, FileStore store, TimeProvider clock)
{
    public async Task<Result<FileObject>> DeclareAsync(string userId, FileDeclaration declaration, CancellationToken ct)
    {
        if (!FileTypePolicy.IsAllowed(declaration.ContentType)) return Result<FileObject>.Fail(ErrorCodes.FileTypeNotAllowed);
        if (declaration.Size <= 0 || declaration.Size > FileStore.MaxFileBytes) return Result<FileObject>.Fail(ErrorCodes.FileTooLarge);
        if (string.IsNullOrWhiteSpace(declaration.Name) || declaration.Name.Length > 256 || declaration.Name.Any(char.IsControl))
            return Result<FileObject>.Fail(ErrorCodes.FileTypeNotAllowed);
        var hash = declaration.Sha256?.ToLowerInvariant();
        if (hash is not null && !FileStore.IsHash(hash)) return Result<FileObject>.Fail(ErrorCodes.FileHashMismatch);
        var now = clock.GetUtcNow();
        var row = new FileObject { TenantId = db.CurrentTenantId, Id = IdGenerator.NewId(now),
            UploadId = IdGenerator.NewId(now), CreatedByUserId = userId, DeclaredSizeBytes = declaration.Size,
            ContentType = declaration.ContentType, OriginalName = Path.GetFileName(declaration.Name.Replace('\\', '/')),
            Sha256 = hash, CreatedUtc = now, UpdatedUtc = now, State = FileState.Staged };
        db.FileObjects.Add(row); await db.SaveChangesAsync(ct).ConfigureAwait(false); return Result<FileObject>.Ok(row);
    }
    // PG advisory locks protect the FS mutation as well as the metadata across processes.
    private Task LockAsync(string key, CancellationToken ct) => db.Database.ExecuteSqlInterpolatedAsync(
        $"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", ct);
    public Task<FileObject?> FindAsync(string id, bool byUpload, CancellationToken ct) => byUpload
        ? db.FileObjects.SingleOrDefaultAsync(x => x.UploadId == id, ct)
        : db.FileObjects.SingleOrDefaultAsync(x => x.Id == id, ct);
    public async Task<ChunkOutcome> WriteAsync(string uploadId, long offset, byte[] bytes, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        await LockAsync("file-upload:" + db.CurrentTenantId + ":" + uploadId, ct).ConfigureAwait(false);
        var row = await FindAsync(uploadId, true, ct).ConfigureAwait(false);
        if (row is null) return new(false, 0, ErrorCodes.FileNotFound);
        await db.Entry(row).ReloadAsync(ct).ConfigureAwait(false);
        if (offset < 0 || bytes.Length == 0 || bytes.Length > FileStore.MaxChunkBytes) return new(false, row.SizeBytes, ErrorCodes.FileTooLarge);
        if (offset < row.SizeBytes && row.State is FileState.Staged or FileState.Uploaded)
        {
            var chunk = await db.FileUploadChunks.SingleOrDefaultAsync(x => x.UploadId == uploadId && x.Offset == offset, ct).ConfigureAwait(false);
            bool same = chunk?.Length == bytes.Length && await store.ChunkEqualsAsync(row.TenantId, uploadId, offset, bytes, ct).ConfigureAwait(false);
            return new(same, row.SizeBytes);
        }
        if (offset != row.SizeBytes || row.State != FileState.Staged) return new(false, row.SizeBytes);
        if (bytes.Length > row.DeclaredSizeBytes - offset) return new(false, row.SizeBytes, ErrorCodes.FileTooLarge);
        await store.WriteChunkAsync(row.TenantId, uploadId, offset, bytes, ct).ConfigureAwait(false);
        db.FileUploadChunks.Add(new() { TenantId = row.TenantId, UploadId = uploadId, Offset = offset, Length = bytes.Length });
        row.SizeBytes += bytes.Length; row.UpdatedUtc = clock.GetUtcNow();
        if (row.SizeBytes == row.DeclaredSizeBytes) row.State = FileState.Uploaded;
        await db.SaveChangesAsync(ct).ConfigureAwait(false); await tx.CommitAsync(ct).ConfigureAwait(false);
        return new(true, row.SizeBytes);
    }
    public async Task<Result<FileObject>> CompleteAsync(string uploadId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        await LockAsync("file-upload:" + db.CurrentTenantId + ":" + uploadId, ct).ConfigureAwait(false);
        var row = await FindAsync(uploadId, true, ct).ConfigureAwait(false);
        if (row is null) return Result<FileObject>.Fail(ErrorCodes.FileNotFound);
        await db.Entry(row).ReloadAsync(ct).ConfigureAwait(false);
        if (row.State == FileState.Available) return Result<FileObject>.Ok(row);
        if (row.State != FileState.Uploaded || row.SizeBytes != row.DeclaredSizeBytes) return Result<FileObject>.Fail(ErrorCodes.FileHashMismatch);
        string staged = store.Staging(row.TenantId, uploadId);
        if (!File.Exists(staged) || new FileInfo(staged).Length != row.DeclaredSizeBytes) return Result<FileObject>.Fail(ErrorCodes.FileHashMismatch);
        byte[] prefix = new byte[(int)Math.Min(512, row.SizeBytes)];
        await using (var stream = File.OpenRead(staged)) await stream.ReadExactlyAsync(prefix, ct).ConfigureAwait(false);
        string hash = await store.HashAsync(staged, ct).ConfigureAwait(false);
        if ((row.Sha256 is not null && row.Sha256 != hash) || !FileTypePolicy.Matches(row.ContentType, prefix))
        {
            bool badType = !FileTypePolicy.Matches(row.ContentType, prefix);
            row.State = FileState.Mismatch; row.UpdatedUtc = clock.GetUtcNow();
            await db.SaveChangesAsync(ct).ConfigureAwait(false); await tx.CommitAsync(ct).ConfigureAwait(false);
            return Result<FileObject>.Fail(badType ? ErrorCodes.FileTypeNotAllowed : ErrorCodes.FileHashMismatch);
        }
        row.Sha256 = hash; row.State = FileState.HashVerified;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await LockAsync("file-blob:" + hash, ct).ConfigureAwait(false);
        await store.PublishAsync(row.TenantId, uploadId, hash, ct).ConfigureAwait(false);
        // Increment and Committed are persisted in this same transaction; CAS orphans are safe to reconcile.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public."BlobRefs" ("Sha256","RefCount","SizeBytes","FirstSeenUtc","UnreferencedUtc")
            VALUES ({hash},1,{row.SizeBytes},{clock.GetUtcNow()},NULL)
            ON CONFLICT ("Sha256") DO UPDATE SET "RefCount"="BlobRefs"."RefCount"+1,"UnreferencedUtc"=NULL
            """, ct).ConfigureAwait(false);
        row.State = FileState.Committed; row.UpdatedUtc = clock.GetUtcNow();
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        row.State = FileState.Available; await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);
        File.Delete(staged); return Result<FileObject>.Ok(row);
    }
    public async Task<Result> DeleteAsync(string id, CancellationToken ct)
    {
        var row = await FindAsync(id, false, ct).ConfigureAwait(false);
        if (row is null) return Result.Fail(ErrorCodes.FileNotFound);
        await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        await LockAsync("file-upload:" + db.CurrentTenantId + ":" + row.UploadId, ct).ConfigureAwait(false);
        await db.Entry(row).ReloadAsync(ct).ConfigureAwait(false);
        if (await db.DocumentReferenceSet.AnyAsync(x => x.FileId == id && !x.Deleted, ct).ConfigureAwait(false))
            return Result.Fail(ErrorCodes.ConcurrencyConflict);
        if (row.State is FileState.Available or FileState.Committed && row.Sha256 is { } hash)
        {
            await LockAsync("file-blob:" + hash, ct).ConfigureAwait(false);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE public."BlobRefs" SET "RefCount"=GREATEST(0,"RefCount"-1),
                "UnreferencedUtc"=CASE WHEN "RefCount"<=1 THEN {clock.GetUtcNow()} ELSE NULL END WHERE "Sha256"={hash}
                """, ct).ConfigureAwait(false);
        }
        // Explicit soft deletion, not a lifecycle recovery edge (Mismatch remains terminal).
        if (row.State == FileState.Mismatch) return Result.Fail(ErrorCodes.FileHashMismatch);
        row.State = FileState.Abandoned; row.UpdatedUtc = clock.GetUtcNow();
        await db.FileUploadChunks.Where(x => x.UploadId == row.UploadId).ExecuteDeleteAsync(ct).ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false); await tx.CommitAsync(ct).ConfigureAwait(false);
        if (row.UploadId is { } upload) File.Delete(store.Staging(row.TenantId, upload));
        return Result.Ok();
    }
}
