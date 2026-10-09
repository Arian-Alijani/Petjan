namespace Animora.Persistence.PostgreSql.Repositories;

/// <summary>
/// The single source of truth for server-side paging limits (CONVENTIONS §5: paging is mandatory, the
/// maximum page size is 200). Every <see cref="TenantRepository{TEntity}"/> read clamps through here so no
/// caller can request an unbounded result set on the weak host.
/// </summary>
public static class PageRules
{
    /// <summary>The largest page a repository will ever return, regardless of the requested size.</summary>
    public const int MaxPageSize = 200;

    /// <summary>The smallest legal page size (a request below this is raised to it).</summary>
    public const int MinPageSize = 1;

    /// <summary>
    /// Clamps a requested page size into <c>[<see cref="MinPageSize"/>, <see cref="MaxPageSize"/>]</c>:
    /// values above 200 become 200, values below 1 become 1.
    /// </summary>
    public static int ClampPageSize(int pageSize) =>
        pageSize < MinPageSize ? MinPageSize
        : pageSize > MaxPageSize ? MaxPageSize
        : pageSize;

    /// <summary>Normalizes a 1-based page number: anything below 1 becomes 1.</summary>
    public static int NormalizePage(int page) => page < 1 ? 1 : page;
}
