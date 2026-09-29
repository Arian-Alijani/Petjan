using System.Globalization;
using Animora.Application.UseCases.History;
using Animora.Contracts.History;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Entities;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.Sqlite.Queries;

/// <summary>Tenant-scoped, indexed feed reads; export never materializes more than 50,001 rows.</summary>
public sealed class ActivityFeedQueries(AnimoraLocalDbContext db) : IActivityFeedQueries
{
    private static int? Day(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var parts = text.Split('/');
        if (parts.Length != 3 || !int.TryParse(parts[0], out var year) ||
            !int.TryParse(parts[1], out var month) || !int.TryParse(parts[2], out var day))
            throw new ArgumentException("Invalid Jalali date.", nameof(text));
        var date = new PersianCalendar().ToDateTime(year, month, day, 0, 0, 0, 0);
        return date.Year * 10000 + date.Month * 100 + date.Day;
    }

    private IQueryable<ActivityEvent> Filter(string tenant, HistoryFilter f)
    {
        var query = db.Set<ActivityEvent>().AsNoTracking().Where(x => x.TenantId == tenant);
        var from = Day(f.FromDate);
        var to = Day(f.ToDate);
        if (from.HasValue) query = query.Where(x => x.LocalDay >= from.Value);
        if (to.HasValue) query = query.Where(x => x.LocalDay <= to.Value);
        var kinds = f.Kinds.Select(x => x.ToString()).ToArray();
        if (kinds.Length > 0) query = query.Where(x => kinds.Contains(x.Kind));
        var search = f.Search?.Trim();
        if (!string.IsNullOrEmpty(search)) query = query.Where(x =>
            x.Title.Contains(search) || x.DetailArgsJson.Contains(search) ||
            x.EntityId.Contains(search) || x.ActorName.Contains(search));
        return query;
    }

    private static IQueryable<ActivityEvent> Sort(IQueryable<ActivityEvent> q, HistorySort sort) => sort switch
    {
        HistorySort.Oldest => q.OrderBy(x => x.LocalDay).ThenBy(x => x.Id),
        HistorySort.AmountDesc => q.OrderByDescending(x => x.AmountMinor).ThenByDescending(x => x.Id),
        HistorySort.AmountAsc => q.OrderBy(x => x.AmountMinor).ThenByDescending(x => x.Id),
        _ => q.OrderByDescending(x => x.LocalDay).ThenByDescending(x => x.Id),
    };

    private static IQueryable<ActivityRow> Project(IQueryable<ActivityEvent> q) => q.Select(x =>
        new ActivityRow(x.Id, x.OccurredUtc, x.Kind, x.Title, x.DetailArgsJson,
            x.EntityType, x.EntityId, x.AmountMinor, x.AmountSign, x.Status,
            x.ActorUserId, x.ActorName));

    public async Task<ActivityPage> PageAsync(string tenantId, HistoryFilter filter, CancellationToken ct)
    {
        var q = Filter(tenantId, filter);
        var totals = await q.GroupBy(x => 1).Select(g => new
        {
            Count = g.Count(),
            Income = g.Sum(x => x.AmountSign > 0 ? x.AmountMinor : 0),
            Expense = g.Sum(x => x.AmountSign < 0 ? x.AmountMinor : 0),
        }).FirstOrDefaultAsync(ct);
        var size = Math.Clamp(filter.PageSize, 1, 200);
        var skip = (int)Math.Min((long)Math.Max(0, filter.Page - 1) * size, int.MaxValue);
        var rows = await Project(Sort(q, filter.Sort).Skip(skip).Take(size)).ToListAsync(ct);
        return new ActivityPage(rows, totals?.Count ?? 0, totals?.Income ?? 0, totals?.Expense ?? 0);
    }

    public async Task<IReadOnlyList<ActivityRow>> ExportAsync(string tenantId, HistoryFilter filter, CancellationToken ct) =>
        await Project(Sort(Filter(tenantId, filter), filter.Sort).Take(50_001)).ToListAsync(ct);
}
