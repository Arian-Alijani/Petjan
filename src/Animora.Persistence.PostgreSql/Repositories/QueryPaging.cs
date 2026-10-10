using Animora.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.PostgreSql.Repositories;

internal static class QueryPaging
{
    public static async Task<PagedResult<T>> ToPagedAsync<T>(this IQueryable<T> query,
        int page, int pageSize, CancellationToken ct)
    {
        page = PageRules.NormalizePage(page);
        pageSize = PageRules.ClampPageSize(pageSize);
        var count = await query.CountAsync(ct);
        var offset = (long)(page - 1) * pageSize;
        IReadOnlyList<T> items = offset >= count ? [] : await query.Skip(checked((int)offset)).Take(pageSize).ToListAsync(ct);
        return new PagedResult<T>(items, count, page, pageSize);
    }
}
