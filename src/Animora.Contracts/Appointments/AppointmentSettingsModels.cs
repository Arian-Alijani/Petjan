namespace Animora.Contracts.Appointments;

/// <summary>How the appointment calendar books time: by clock slot or by whole day only.</summary>
public enum CalendarMode
{
    /// <summary>Appointments occupy a start time + duration (the default time-grid behaviour).</summary>
    TimeBased,

    /// <summary>Appointments are booked for a date only — no clock time, no slot grid.</summary>
    DayOnly,
}

/// <summary>
/// A clinic-defined appointment type. <see cref="ColorHex"/> is chosen once when the type is
/// created and persisted — it is never re-randomized at render time.
/// </summary>
public sealed record CustomAppointmentType(
    string Id,
    string Label,
    string ColorHex,
    int DurationMinutes,
    bool RequiresDoctor);

/// <summary>SMS preferences for appointments: booking confirmation + reminder lead time.</summary>
public sealed record AppointmentSmsSettings(
    bool SendOnBooking,
    bool SendReminder,
    int ReminderLeadHours)
{
    public static AppointmentSmsSettings Default { get; } = new(true, true, 24);
}

/// <summary>The persisted, clinic-editable appointment preferences.</summary>
public sealed record AppointmentSettings(
    CalendarMode CalendarMode,
    AppointmentSmsSettings Sms,
    IReadOnlyList<CustomAppointmentType> CustomTypes,
    string? RecoveryNotice = null)
{
    public static AppointmentSettings Default { get; } =
        new(CalendarMode.TimeBased, AppointmentSmsSettings.Default, []);
}
