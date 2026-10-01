namespace Animora.Contracts.Appointments;

/// <summary>
/// Seam for the clinic-editable appointment preferences (custom types with color, calendar mode,
/// SMS options). Local, offline-first preference storage — not transactional clinic data.
/// </summary>
public interface IAppointmentSettingsService
{
    /// <summary>Current settings; defaults when nothing has been saved yet.</summary>
    Task<AppointmentSettings> GetAsync();

    /// <summary>Replaces all settings.</summary>
    Task SaveAsync(AppointmentSettings settings);

    /// <summary>Adds a custom type; the service picks and persists a distinct color.</summary>
    Task<CustomAppointmentType> AddTypeAsync(string label, int durationMinutes, bool requiresDoctor);

    /// <summary>Removes a custom type by id (no-op when unknown).</summary>
    Task RemoveTypeAsync(string typeId);

    Task SetCalendarModeAsync(CalendarMode mode);

    Task SetSmsAsync(AppointmentSmsSettings sms);
}
