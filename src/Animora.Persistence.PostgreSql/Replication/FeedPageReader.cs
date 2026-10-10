using Animora.Persistence.PostgreSql.Context;
using Microsoft.EntityFrameworkCore;
namespace Animora.Persistence.PostgreSql.Replication;

public sealed record FeedPageDownload(Stream Content, string Hash, long Length);
public sealed class FeedPageReader(IFeedContextFactory factory, FeedPageWriter writer)
{
    public async Task<FeedPageDownload?> OpenAsync(string batch, int ordinal, CancellationToken ct)
    {
        if (ordinal < 0) return null;
        try { FeedPageWriter.ValidateSegment(batch); } catch (ArgumentException) { return null; }
        await using var db = factory.Create();
        var page = await (from p in db.Set<FeedBatchPageRow>()
            join b in db.Set<FeedBatchRow>() on new { p.TenantId, p.BatchId } equals new { b.TenantId, b.BatchId }
            join f in db.Set<TenantFeedStateRow>() on b.TenantId equals f.TenantId
            where p.BatchId == batch && p.PageOrdinal == ordinal && b.State == FeedBatchState.Ready && b.Epoch == f.Epoch && b.Generation == f.Generation
            select p).SingleOrDefaultAsync(ct).ConfigureAwait(false);
        if (page is null) return null;
        var path = Path.Combine(writer.BatchDirectory(db.CurrentTenantId, batch), $"{ordinal}.gz");
        try { var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 65536, true);
            return new(stream, page.PageHash, stream.Length); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }
}
