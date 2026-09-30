using Animora.Application.Abstractions;
using Animora.Application.Dispatch;
using Animora.Application.Security;
using Animora.Application.UseCases.Scheduling;
using Animora.Contracts.Appointments;
using Microsoft.Extensions.DependencyInjection;

namespace Animora.Client.Services.Appointments;

/// <summary>Booking client seam: no EF or business rule in the presentation layer.</summary>
public sealed class AppointmentBookingService(IServiceScopeFactory scopes, AppointmentsQueryService calendar)
    : IAppointmentBookingService
{
    private Task<T> QueryAsync<T>(IQuery<T> query) => calendar.QueryAsync(query);

    private async Task<T> SendAsync<T>(ICommand<T> command)
    {
        await using var scope = scopes.CreateAsyncScope();
        var provider = scope.ServiceProvider.GetRequiredService<ICurrentUserProvider>();
        var context = await provider.BuildCommandContextAsync(null, null, null, default);
        if (context.IsFailure) throw new InvalidOperationException(context.Error.Code);
        var result = await scope.ServiceProvider.GetRequiredService<IDispatcher>().SendAsync(command, context.Value!);
        if (result.IsFailure) throw new InvalidOperationException(result.Error.Code);
        return result.Value!;
    }

    public async Task<AppointmentDetail?> GetDetailAsync(string appointmentId)
    {
        var row = await QueryAsync(new AppointmentDetailQuery(appointmentId));
        return row is null ? null : new AppointmentDetail(row.Event.ToContract(calendar.GetTimeZone()), row.CreatedText);
    }

    public async Task<IReadOnlyList<BookingPatient>> SearchPatientsAsync(
        string search, string speciesKey, CancellationToken ct = default) =>
        (await calendar.QueryAsync(new BookingPatientsQuery(search, speciesKey), ct))
            .Select(r => r.ToContract()).ToArray();

    public async Task<BookingPatient?> GetAppointmentPatientAsync(string appointmentId) =>
        (await QueryAsync(new BookingPatientsQuery(AppointmentId: appointmentId)))
            .SingleOrDefault()?.ToContract();

    public async Task<BookingPatient> CreatePatientAsync(NewPatientRequest request)
    {
        var id = await SendAsync(new CreateBookingPatientCommand(request.Name, request.Kind, request.OwnerName, request.Phone));
        return (await QueryAsync(new BookingPatientsQuery(PatientId: id))).Single().ToContract();
    }

    public async Task<IReadOnlyList<TimeSlot>> GetAvailableSlotsAsync(string doctorId, DateOnly date, int durationMinutes) =>
        (await QueryAsync(new AvailableSlotsQuery(doctorId, date, durationMinutes)))
            .Select(slot => new TimeSlot(slot.Minutes, slot.IsFree)).ToArray();

    public async Task<IReadOnlyList<AppointmentEvent>> GetConflictsAsync(
        DateOnly date, string doctorId, int startMinutes, int durationMinutes, string? exceptId)
    {
        var events = await calendar.GetForRangeAsync(date, date);
        return events.Where(e => e.Id != exceptId && e.DoctorId == doctorId &&
            e.Status is AppointmentStatus.Pending or AppointmentStatus.Confirmed &&
            e.StartMinutes < startMinutes + durationMinutes && startMinutes < e.EndMinutes).ToArray();
    }

    public Task<IReadOnlyDictionary<DateOnly, int>> GetDayCountsAsync(DateOnly start, DateOnly end) =>
        QueryAsync(new AppointmentDayCountsQuery(start, end));

    public Task<int> GetDoctorBusyCountAsync(string doctorId, DateOnly date) =>
        QueryAsync(new DoctorBusyCountQuery(doctorId, date));

    public async Task<AppointmentEvent> CreateAsync(NewAppointmentRequest request)
    {
        var patient = await ResolvePatientAsync(request.PatientId);
        var id = await SendAsync(new BookAppointmentCommand(request.Date, request.StartMinutes,
            request.DurationMinutes, request.TypeId, request.DoctorId, patient.Id, patient.OwnerId, request.Note));
        return (await GetDetailAsync(id))!.Event;
    }

    public async Task<AppointmentEvent> UpdateAsync(string appointmentId, UpdateAppointmentRequest request)
    {
        await SendAsync(new RescheduleAppointmentCommand(appointmentId, request.Date,
            request.StartMinutes, request.DurationMinutes));
        return (await GetDetailAsync(appointmentId))!.Event;
    }

    public async Task CancelAsync(string appointmentId) =>
        _ = await SendAsync(new CancelAppointmentCommand(appointmentId, "cancelled by reception"));

    public async Task CheckInAsync(string appointmentId) =>
        _ = await SendAsync(new CheckInAppointmentCommand(appointmentId));

    private async Task<Animora.Application.UseCases.Scheduling.ReadModels.BookingPatientReadModel> ResolvePatientAsync(
        string patientId)
    {
        if (string.IsNullOrWhiteSpace(patientId)) throw new InvalidOperationException("Patient not selected.");
        // Resolve the selected identity directly; duplicate names and records beyond
        // the old 500-row window must not cause incorrect or ambiguous bookings.
        return (await QueryAsync(new BookingPatientsQuery(PatientId: patientId))).SingleOrDefault()
            ?? throw new InvalidOperationException("Patient not found.");
    }
}
