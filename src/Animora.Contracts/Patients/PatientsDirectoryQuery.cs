namespace Animora.Contracts.Patients;

/// <summary>
/// Stable sort order for the directory, mirroring the toolbar's sort select
/// (recent | name | age). Pushed through to the server-side <c>ORDER BY</c> so
/// paging never drifts between requests.
/// </summary>
public enum PatientsSortOrder
{
    /// <summary>Owners: newest registration first; pets: newest last-visit first.</summary>
    Recent,

    /// <summary>Display name A–Z.</summary>
    Name,

    /// <summary>Owners: oldest registration first; pets: oldest birth date (age).</summary>
    Age,
}

/// <summary>
/// Vaccination filter, mirroring the toolbar's <c>vax</c> select (all | has | none).
/// </summary>
public enum VaccineFilter
{
    All,
    Has,
    None,
}

/// <summary>
/// The full directory request built from the toolbar state. Search, species/vaccine filtering,
/// sorting and paging all run server-side; the caller materializes only the returned page.
/// </summary>
/// <param name="Search">Free-text term (owner/pet fields); null or empty matches all.</param>
/// <param name="Species">Species facet filter, or null for every species.</param>
/// <param name="Vaccine">Vaccination filter (no data source yet in C01; <c>Has</c> matches nothing).</param>
/// <param name="Sort">Stable sort order.</param>
/// <param name="Page">1-based page index.</param>
/// <param name="PageSize">Requested rows per page (clamped to the server hard ceiling of 200).</param>
public sealed record PatientsDirectoryQuery(
    string? Search,
    PetSpecies? Species,
    VaccineFilter Vaccine,
    PatientsSortOrder Sort,
    int Page,
    int PageSize);

/// <summary>
/// One bounded page of directory rows plus the total match count for the active query, so the
/// caller can compute page math without ever holding the whole dataset in memory.
/// </summary>
public sealed record PatientsPage<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize)
{
    /// <summary>An empty page for the given paging coordinates (used for no-op filters).</summary>
    public static PatientsPage<T> Empty(int page, int pageSize) => new([], 0, page, pageSize);
}

/// <summary>
/// Directory counters for the active query: total owners, total pets, and per-species pet counts.
/// Drives the result caption and the species chip rail without materializing the dataset. The
/// species breakdown ignores the active species filter so the chips always show the full taxonomy.
/// </summary>
public sealed record PatientsDirectoryFacets(
    int TotalOwners,
    int TotalPets,
    IReadOnlyDictionary<PetSpecies, int> SpeciesCounts);
