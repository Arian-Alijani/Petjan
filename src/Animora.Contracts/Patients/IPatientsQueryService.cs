namespace Animora.Contracts.Patients;

/// <summary>
/// Read-only data seam for the Patients &amp; Owners directory. The ViewModel
/// depends only on this interface; the in-memory <c>MockPatientsQueryService</c>
/// is the sole swap point for a future backend implementation (R-18).
/// </summary>
public interface IPatientsQueryService
{
    /// <summary>All animals in the directory (unfiltered, unsorted).</summary>
    Task<IReadOnlyList<PetSummary>> GetPetsAsync();

    /// <summary>All owners in the directory (unfiltered, unsorted).</summary>
    Task<IReadOnlyList<OwnerSummary>> GetOwnersAsync();
}
