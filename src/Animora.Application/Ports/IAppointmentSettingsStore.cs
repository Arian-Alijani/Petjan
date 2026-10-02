using Animora.Contracts.Appointments;

namespace Animora.Application.Ports;

/// <summary>
/// Durable store for the clinic-editable appointment preferences (calendar mode, SMS intent, custom types).
/// Implemented outside Application (local file beside the encrypted DB). Every method is truly asynchronous and
/// writes are serialized by the implementation; authorization is decided by the use cases above this port,
/// never here.
/// </summary>
public interface IAppointmentSettingsStore
{
    /// <summary>Current settings; defaults when nothing was saved. Corrupt data is quarantined and reported via <see cref="AppointmentSettings.RecoveryNotice"/>.</summary>
    Task<AppointmentSettings> GetAsync(CancellationToken ct);

    /// <summary>Replaces all settings.</summary>
    Task SaveAsync(AppointmentSettings settings, CancellationToken ct);

    /// <summary>Adds a custom type and returns it with its persisted accent color.</summary>
    Task<CustomAppointmentType> AddTypeAsync(string label, int durationMinutes, bool requiresDoctor, CancellationToken ct);

    /// <summary>Removes a custom type by id (no-op when unknown).</summary>
    Task RemoveTypeAsync(string typeId, CancellationToken ct);

    Task SetCalendarModeAsync(CalendarMode mode, CancellationToken ct);

    Task SetSmsAsync(AppointmentSmsSettings sms, CancellationToken ct);

    /// <summary>
    /// Writes a copy of the persisted settings to <paramref name="destinationPath"/> so a database backup carries the
    /// clinic's custom types / mode with it (A26). Returns false when nothing was ever customised (no file to copy).
    /// </summary>
    Task<bool> ExportToAsync(string destinationPath, CancellationToken ct);

    /// <summary>
    /// Replaces the persisted settings with the copy at <paramref name="sourcePath"/> after a database restore (A26).
    /// The previous file is kept aside, never deleted; an unreadable source leaves the current settings untouched and
    /// returns false.
    /// </summary>
    Task<bool> ImportFromAsync(string sourcePath, CancellationToken ct);
}
