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

    /// <summary>
    /// A29: the caller-observed version token rides into the durable journal envelope. A mutation of an existing
    /// appointment passes what it read; a new aggregate (booking) deliberately passes none. The token is opaque -
    /// only a numeric token can be journaled, anything else is dropped rather than guessed.
    /// </summary>
    private static long? JournalVersion(string? token) =>
        long.TryParse(token, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var v)
            ? v : null;

    private async Task<T> SendAsync<T>(ICommand<T> command, string? expectedVersion = null)
    {
        await using var scope = scopes.CreateAsyncScope();
        var provider = scope.ServiceProvider.GetRequiredService<ICurrentUserProvider>();
        var context = await provider.BuildCommandContextAsync(null, null, JournalVersion(expectedVersion), default);
        if (context.IsFailure)
            throw new AppointmentOperationException(context.Error.Code, context.Error.Category, context.Detail);
        var result = await scope.ServiceProvider.GetRequiredService<IDispatcher>().SendAsync(command, context.Value!);
        if (result.IsFailure)
            throw new AppointmentOperationException(result.Error.Code, result.Error.Category, result.Detail);
        return result.Value!;
    }

    public async Task<AppointmentDetail?> GetDetailAsync(string appointmentId)
    {
        var row = await QueryAsync(new AppointmentDetailQuery(appointmentId));
        if (row is null) return null;
        // A28: the creator is a real lookup; an unknown creator is labelled as such, never guessed.
        var created = string.IsNullOrWhiteSpace(row.CreatedByName) ? "نامشخص" : row.CreatedByName!;
        return new AppointmentDetail(row.Event.ToContract(await calendar.GetTimeZoneAsync()), created,
            row.AggregateVersion);
    }

    public async Task<IReadOnlyList<BookingPatient>> SearchPatientsAsync(
        string search, string speciesKey, CancellationToken ct = default) =>
        (await calendar.QueryAsync(new BookingPatientsQuery(search, speciesKey), ct))
            .Select(r => r.ToContract()).ToArray();

    public async Task<BookingPatient?> GetPatientAsync(string patientId) =>
        string.IsNullOrWhiteSpace(patientId)
            ? null
            : (await QueryAsync(new BookingPatientsQuery(PatientId: patientId))).SingleOrDefault()?.ToContract();

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
        // A21: read only this doctor's rows for the day instead of every doctor's events.
        var tz = await calendar.GetTimeZoneAsync();
        var events = (await QueryAsync(new AppointmentRangeQuery(date, date, doctorId))).Select(r => r.ToContract(tz));
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
        return await ReadCommittedAsync(id);
    }

    public async Task<AppointmentEvent> UpdateAsync(string appointmentId, UpdateAppointmentRequest request)
    {
        await SendAsync(new RescheduleAppointmentCommand(appointmentId, request.Date,
            request.StartMinutes, request.DurationMinutes), request.ExpectedVersion);
        return await ReadCommittedAsync(appointmentId);
    }

    /// <summary>
    /// Re-reads a just-written appointment. The write is already durable here, so a failing read is reported as
    /// "committed" (A10) instead of masquerading as a failed write.
    /// </summary>
    private async Task<AppointmentEvent> ReadCommittedAsync(string appointmentId)
    {
        try
        {
            return (await GetDetailAsync(appointmentId))?.Event
                ?? throw new AppointmentOperationException("APPOINTMENT_NOT_FOUND", "NotFound", writeCommitted: true);
        }
        catch (AppointmentOperationException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new AppointmentOperationException("APPOINTMENT_READ_FAILED", "Infrastructure", ex.Message,
                writeCommitted: true);
        }
    }

    public async Task CancelAsync(string appointmentId, string? expectedVersion = null) =>
        _ = await SendAsync(new CancelAppointmentCommand(appointmentId, "cancelled by reception"), expectedVersion);

    public async Task CheckInAsync(string appointmentId, string? expectedVersion = null) =>
        _ = await SendAsync(new CheckInAppointmentCommand(appointmentId), expectedVersion);

    private async Task<Animora.Application.UseCases.Scheduling.ReadModels.BookingPatientReadModel> ResolvePatientAsync(
        string patientId)
    {
        if (string.IsNullOrWhiteSpace(patientId)) throw new AppointmentOperationException("PATIENT_NOT_SELECTED", "Validation");
        // Resolve the selected identity directly; duplicate names and records beyond
        // the old 500-row window must not cause incorrect or ambiguous bookings.
        return (await QueryAsync(new BookingPatientsQuery(PatientId: patientId))).SingleOrDefault()
            ?? throw new AppointmentOperationException("PATIENT_NOT_FOUND", "NotFound");
    }
}
