using Animora.Domain.Primitives;
using Animora.Persistence.PostgreSql.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.PostgreSql.Repositories;

/// <summary>
/// Generic base for every server-side tenant-scoped repository (concrete aggregates arrive in S07–S10).
/// Reads default to <see cref="QueryTrackingBehavior.NoTracking"/> and are always paged (max
/// <see cref="PageRules.MaxPageSize"/>); writes go through the change tracker and are committed by
/// <c>ServerUnitOfWork</c>. All queries run through the context's filtered <see cref="Set"/>, so the
/// <c>"Tenant"</c> and <c>"SoftDelete"</c> named filters always apply — the base <b>never</b> calls
/// <c>IgnoreQueryFilters</c>, and no un-paged <c>ToListAsync()</c> overload is exposed. Deletion is soft
/// only; hard delete is not surfaced.
/// </summary>
/// <typeparam name="TEntity">The tenant-scoped, syncable aggregate this repository manages.</typeparam>
public abstract class TenantRepository<TEntity>
    where TEntity : SyncableEntity
{
    private readonly AnimoraServerDbContext _context;

    /// <summary>Creates the repository over the request-scoped OLTP context.</summary>
    /// <param name="context">The server OLTP context (tenant filter already bound).</param>
    protected TenantRepository(AnimoraServerDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    /// <summary>The filtered set for <typeparamref name="TEntity"/> (tenant + soft-delete filters applied).</summary>
    protected DbSet<TEntity> Set => _context.Set<TEntity>();

    /// <summary>
    /// Loads a single entity by id <b>tracked</b>, for a subsequent update. Returns <see langword="null"/>
    /// when no visible row matches (filtered out by tenant/soft-delete or absent).
    /// </summary>
    protected Task<TEntity?> GetAsync(string id, CancellationToken ct = default) =>
        Set.FirstOrDefaultAsync(e => e.Id == id, ct);

    /// <summary>
    /// Loads a single entity by id with <b>no tracking</b>, for read-only display. Returns
    /// <see langword="null"/> when no visible row matches.
    /// </summary>
    protected Task<TEntity?> ReadAsync(string id, CancellationToken ct = default) =>
        Set.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct);

    /// <summary>
    /// Applies mandatory paging to a query: normalizes the 1-based <paramref name="page"/> and clamps
    /// <paramref name="pageSize"/> to <c>[1, <see cref="PageRules.MaxPageSize"/>]</c> before
    /// <c>Skip</c>/<c>Take</c>. The caller supplies an ordered query and reads it with
    /// <c>AsNoTracking()</c>/projection.
    /// </summary>
    protected static IQueryable<TEntity> Page(IQueryable<TEntity> query, int page, int pageSize)
    {
        ArgumentNullException.ThrowIfNull(query);

        int normalizedPage = PageRules.NormalizePage(page);
        int clampedSize = PageRules.ClampPageSize(pageSize);
        return query.Skip((normalizedPage - 1) * clampedSize).Take(clampedSize);
    }

    /// <summary>Stages a new entity for insertion (tenant id is stamped from context on save).</summary>
    protected void Add(TEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        Set.Add(entity);
    }

    /// <summary>
    /// Soft-deletes an entity by setting its tombstone; the row is kept and replicated so peers converge.
    /// Hard delete is intentionally not exposed.
    /// </summary>
    protected static void SoftDelete(TEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        entity.Deleted = true;
    }
}
