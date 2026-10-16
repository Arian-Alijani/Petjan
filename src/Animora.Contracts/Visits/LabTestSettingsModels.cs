namespace Animora.Contracts.Visits;

/// <summary>
/// One clinic-configurable laboratory test type («نوع آزمایش») offered by the visit's lab form, e.g.
/// «گلبول سفید (WBC)» with its usual unit. Managed in Visits → تنظیمات.
/// </summary>
/// <param name="Id">Stable id; lab results keep it (plus the name at recording time) so history survives a later removal.</param>
/// <param name="Name">Display name.</param>
/// <param name="DefaultUnit">Usual measurement unit pre-filled in the lab form; <see langword="null"/> when none.</param>
public sealed record LabTestType(string Id, string Name, string? DefaultUnit);

/// <summary>
/// Seam for the clinic's laboratory test-type catalog. Local preference storage (not replicated
/// business data): reads need <c>visits.read</c>, every mutation needs <c>settings.write</c>.
/// </summary>
public interface ILabTestSettingsService
{
    /// <summary>All configured test types in display order (defaults when nothing was customised yet).</summary>
    Task<IReadOnlyList<LabTestType>> GetTypesAsync();

    /// <summary>Adds a test type and returns it; a name that already exists (case-insensitive) is rejected.</summary>
    Task<LabTestType> AddTypeAsync(string name, string? defaultUnit);

    /// <summary>Removes a test type by id (no-op when unknown). Existing lab results are unaffected.</summary>
    Task RemoveTypeAsync(string typeId);
}
