namespace Animora.Contracts.Patients;

/// <summary>
/// Read/write data seam for the Patients &amp; Owners directory and the drill-in
/// detail screens (Owner Sheet, Pet File, add owner/pet wizard). The ViewModels
/// depend only on this interface; the C01 <c>PatientsService</c> (Client.Services)
/// implements it over the real local store use cases, and the in-memory mock it
/// replaced is deleted (R-18).
/// </summary>
public interface IPatientsQueryService
{
    /// <summary>
    /// One bounded page of pets matching <paramref name="query"/>. Search, species/vaccine
    /// filtering, sorting and paging all run server-side; the caller materializes only the
    /// returned slice (never the whole table). <see cref="PatientsPage{T}.TotalCount"/> reflects
    /// every match for the page math + result counter.
    /// </summary>
    Task<PatientsPage<PetSummary>> GetPetsPageAsync(PatientsDirectoryQuery query);

    /// <summary>
    /// One bounded page of owners matching <paramref name="query"/>. When a species/vaccine
    /// filter is active the owner set is narrowed server-side to those with a matching pet, so the
    /// ViewModel does no cross-entity filtering in RAM.
    /// </summary>
    Task<PatientsPage<OwnerSummary>> GetOwnersPageAsync(PatientsDirectoryQuery query);

    /// <summary>
    /// Directory counters for the active query (total owners, total pets, per-species pet facet
    /// counts) resolved server-side in one call — drives the result caption and the species chip
    /// rail without holding the dataset in memory. Species facets ignore the species filter so the
    /// chips always show the full taxonomy breakdown under the current search/vaccine filter.
    /// </summary>
    Task<PatientsDirectoryFacets> GetDirectoryFacetsAsync(PatientsDirectoryQuery query);

    /// <summary>Full owner record (profile + live pet roster) for the Owner Sheet, or null when unknown.</summary>
    Task<OwnerDetail?> GetOwnerAsync(string id);

    /// <summary>Full pet record (profile + weight/visit/vaccination/document collections) for the Pet File, or null when unknown.</summary>
    Task<PetDetail?> GetPetAsync(string id);

    /// <summary>Persists an edited owner profile; returns the updated record.</summary>
    Task<OwnerDetail> SaveOwnerAsync(OwnerDetail owner);

    /// <summary>Persists edited pet profile fields; returns the updated record.</summary>
    Task<PetDetail> SavePetInfoAsync(PetDetail pet);

    /// <summary>Appends a weight measurement to a pet's growth history; returns the updated record.</summary>
    Task<PetDetail> AddWeightEntryAsync(string petId, WeightEntry entry);

    /// <summary>Creates a new pet under an existing owner; returns the created record.</summary>
    Task<PetDetail> CreatePetAsync(string ownerId, NewPetRequest request);

    /// <summary>Creates a new owner (and optionally their first pet); returns the created owner.</summary>
    Task<OwnerDetail> CreateOwnerAsync(NewOwnerRequest request);
}
