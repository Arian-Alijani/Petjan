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
    /// <summary>All animals in the directory (unfiltered, unsorted).</summary>
    Task<IReadOnlyList<PetSummary>> GetPetsAsync();

    /// <summary>All owners in the directory (unfiltered, unsorted).</summary>
    Task<IReadOnlyList<OwnerSummary>> GetOwnersAsync();

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
