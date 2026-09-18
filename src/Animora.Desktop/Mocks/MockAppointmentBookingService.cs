using Animora.Contracts.Appointments;

namespace Animora.Desktop.Mocks;

/// <summary>
/// In-memory implementation of <see cref="IAppointmentBookingService"/>,
/// mirroring the reference's booking/detail panel controller
/// (Animora-UI/assets/appointments-panel.js). Shares the Phase 13
/// <see cref="AppointmentStore"/> so a booking created/edited/cancelled here is
/// immediately visible on the calendar — the single swap point for a future
/// backend (R-18).
/// </summary>
public sealed class MockAppointmentBookingService : IAppointmentBookingService
{
    private readonly AppointmentStore _store;

    public MockAppointmentBookingService(AppointmentStore store) => _store = store;

    public Task<AppointmentDetail?> GetDetailAsync(string appointmentId)
    {
        var ev = _store.Get(appointmentId);
        return Task.FromResult(ev is null
            ? null
            : new AppointmentDetail(ev, "ثبت توسط پذیرش"));
    }

    public Task<IReadOnlyList<BookingPatient>> GetPatientsAsync() =>
        Task.FromResult(_store.Patients);

    public Task<BookingPatient> CreatePatientAsync(NewPatientRequest request) =>
        Task.FromResult(_store.AddPatient(request));

    public Task<IReadOnlyList<TimeSlot>> GetAvailableSlotsAsync(string doctorId, DateOnly date, int durationMinutes) =>
        Task.FromResult(_store.FreeSlots(date, doctorId, durationMinutes));

    public Task<IReadOnlyList<AppointmentEvent>> GetConflictsAsync(
        DateOnly date, string doctorId, int startMinutes, int durationMinutes, string? exceptId) =>
        Task.FromResult(_store.Conflicts(date, doctorId, startMinutes, durationMinutes, exceptId));

    public Task<IReadOnlyDictionary<DateOnly, int>> GetDayCountsAsync(DateOnly start, DateOnly end) =>
        Task.FromResult(_store.DayCounts(start, end));

    public Task<int> GetDoctorBusyCountAsync(string doctorId, DateOnly date) =>
        Task.FromResult(_store.DoctorBusyCount(doctorId, date));

    public Task<AppointmentEvent> CreateAsync(NewAppointmentRequest request) =>
        Task.FromResult(_store.Create(request));

    public Task<AppointmentEvent> UpdateAsync(string appointmentId, UpdateAppointmentRequest request) =>
        Task.FromResult(_store.Update(appointmentId, request));

    public Task CancelAsync(string appointmentId)
    {
        _store.SetStatus(appointmentId, AppointmentStatus.Cancelled);
        return Task.CompletedTask;
    }

    public Task CheckInAsync(string appointmentId)
    {
        _store.SetStatus(appointmentId, AppointmentStatus.Done);
        return Task.CompletedTask;
    }
}
