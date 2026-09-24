using System.Linq.Expressions;
using Animora.Application.Abstractions;
using Animora.Persistence.Sqlite.Options;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.Sqlite.Queries;

/// <summary>Bounded, stable paging over EF queries (PERFORMANCE.md §3).</summary>
public static class QueryPaging
{
    /// <summary>Absolute ceiling regardless of configuration.</summary>
    public const int HardMaxPageSize = 200;

    /// <summary>
    /// Pages an ordered, no-tracking query into a <see cref="PagedResult{T}"/>. Issues exactly two SQL commands by
    /// design: <c>COUNT</c> and one bounded <c>LIMIT/OFFSET</c> select.
    /// </summary>
    /// <param name="query">Tenant-filtered query that already carries a stable <c>OrderBy</c> (plus a unique tiebreaker).</param>
    /// <param name="page">1-based page; values below 1 are rejected.</param>
    /// <param name="pageSize">Requested size; clamped to <c>[1, min(options.MaxPageSize, 200)]</c>.</param>
    /// <param name="options">Store options providing <see cref="LocalStoreOptions.MaxPageSize"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The page items, total row count and the effective page / page size.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="page"/> is below 1.</exception>
    /// <exception cref="InvalidOperationException">The query has no <c>OrderBy</c>.</exception>
    public static async Task<PagedResult<T>> ToPagedAsync<T>(
        this IQueryable<T> query, int page, int pageSize, LocalStoreOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        if (!OrderingFinder.HasOrdering(query.Expression))
            throw new InvalidOperationException("Paging requires a stable ORDER BY before Skip/Take.");

        var max = Math.Clamp(options.MaxPageSize, 1, HardMaxPageSize);
        var size = Math.Clamp(pageSize, 1, max);
        var total = await query.CountAsync(ct);
        var skip = (long)(page - 1) * size;
        IReadOnlyList<T> items = skip >= total
            ? []
            : await query.Skip((int)skip).Take(size).ToListAsync(ct);
        return new PagedResult<T>(items, total, page, size);
    }

    private sealed class OrderingFinder : ExpressionVisitor
    {
        private bool _found;

        public static bool HasOrdering(Expression expression)
        {
            var finder = new OrderingFinder();
            finder.Visit(expression);
            return finder._found;
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Method.DeclaringType == typeof(Queryable) && node.Method.Name is
                    nameof(Queryable.OrderBy) or nameof(Queryable.OrderByDescending) or
                    nameof(Queryable.ThenBy) or nameof(Queryable.ThenByDescending))
            {
                _found = true;
                return node;
            }
            return base.VisitMethodCall(node);
        }
    }
}
