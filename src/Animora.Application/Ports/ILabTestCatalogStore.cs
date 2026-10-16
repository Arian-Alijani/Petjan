using Animora.Contracts.Visits;

namespace Animora.Application.Ports;

/// <summary>
/// Durable store for the clinic-editable laboratory test-type catalog («انواع آزمایش»). Implemented outside
/// Application (local JSON file beside the encrypted DB, mirroring <see cref="IAppointmentSettingsStore"/>).
/// Writes are serialized by the implementation; authorization is decided by the use cases above this port.
/// </summary>
public interface ILabTestCatalogStore
{
    /// <summary>The configured types; the built-in defaults when nothing was saved. Corrupt data is quarantined and defaults served.</summary>
    Task<IReadOnlyList<LabTestType>> GetAsync(CancellationToken ct);

    /// <summary>Adds a type; returns <see langword="null"/> when a type with the same name already exists.</summary>
    Task<LabTestType?> AddTypeAsync(string name, string? defaultUnit, CancellationToken ct);

    /// <summary>Removes a type by id (no-op when unknown).</summary>
    Task RemoveTypeAsync(string typeId, CancellationToken ct);

    /// <summary>Resets to the built-in defaults (a clinic wipe must not leak the previous clinic's catalog).</summary>
    Task ResetAsync(CancellationToken ct);

    /// <summary>Copies the persisted catalog to <paramref name="destinationPath"/> (database backup sidecar); false when never customised.</summary>
    Task<bool> ExportToAsync(string destinationPath, CancellationToken ct);

    /// <summary>Replaces the persisted catalog with the copy at <paramref name="sourcePath"/> after a restore; false (current kept) when unreadable.</summary>
    Task<bool> ImportFromAsync(string sourcePath, CancellationToken ct);
}
