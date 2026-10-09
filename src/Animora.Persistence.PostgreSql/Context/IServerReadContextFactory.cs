namespace Animora.Persistence.PostgreSql.Context;

/// <summary>
/// Produces short-lived, read-only <see cref="AnimoraServerDbContext"/> instances bound to the
/// <c>oltp-read</c> data source for no-tracking queries. The returned context carries the same tenant
/// filter, soft-delete filter and <c>IgnoreQueryFilters</c> ban as the write context, so isolation holds on
/// the read path too. The caller owns the lifetime and must dispose it. Reporting/insight gets its own
/// budget in S20; this factory is for OLTP reads only.
/// </summary>
public interface IServerReadContextFactory
{
    /// <summary>
    /// Creates a new no-tracking context on the read pool. The caller disposes it
    /// (<c>await using</c>).
    /// </summary>
    AnimoraServerDbContext Create();
}
