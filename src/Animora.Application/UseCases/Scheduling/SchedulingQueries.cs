using Animora.Application.Abstractions;
using Animora.Application.Ports;
using Animora.Application.Security;
using Animora.Application.Time;
using Animora.Application.UseCases.Scheduling.ReadModels;
using Animora.Domain.Identity;
using Animora.Domain.Results;
using Animora.Domain.Scheduling;
using Animora.Domain.Time;

namespace Animora.Application.UseCases.Scheduling;

internal static class SchedulingQueryWork
{
    internal static bool InvalidRange(DateOnly start, DateOnly end) =>
        end.DayNumber < start.DayNumber || end.DayNumber - start.DayNumber > 62;

    internal static IReadOnlyList<TimeSlotReadModel> Available(
        DateOnly day, int duration, (int WorkStartMinute, int WorkEndMinute, int WorkDaysMask) hours,
        IReadOnlyList<ExistingSlot> occupied, TimeZoneInfo tz, string doctorId)
    {
        var weekdayBit = ((int)day.DayOfWeek + 1) % 7; // Saturday=0, Friday=6
        if ((hours.WorkDaysMask & (1 << weekdayBit)) == 0 || duration < Appointment.MinDurationMinutes ||
            duration > Appointment.MaxDurationMinutes || duration % Appointment.DurationStepMinutes != 0)
            return [];
        return SlotGrid.StartsWithin(hours.WorkStartMinute, hours.WorkEndMinute, duration)
            .Select(minutes => new TimeSlotReadModel(minutes,
                SlotConflictPolicy.EnsureFree(new AppointmentSlot(
                    new DateTimeOffset(ClinicCalendar.ToUtcTicks(day, minutes, tz), TimeSpan.Zero), duration),
                    doctorId, occupied).IsSuccess))
            .ToArray();
    }
}

[RequiresPermission(PermissionCodes.AppointmentsRead)]
public sealed record AppointmentRangeQuery(DateOnly Start, DateOnly End, string? DoctorId = null)
    : IQuery<IReadOnlyList<AppointmentEventReadModel>>;
public sealed class AppointmentRangeQueryHandler(IAppointmentQueries queries, IClinicTimeZoneProvider zones)
    : IQueryHandler<AppointmentRangeQuery, IReadOnlyList<AppointmentEventReadModel>>
{
    public async Task<Result<IReadOnlyList<AppointmentEventReadModel>>> HandleAsync(AppointmentRangeQuery q, QueryContext context, CancellationToken ct)
    {
        if (SchedulingQueryWork.InvalidRange(q.Start, q.End))
            return Result<IReadOnlyList<AppointmentEventReadModel>>.Fail(ErrorCodes.AppointmentRangeTooWide);
        var tz = zones.GetClinicTimeZone();
        return Result<IReadOnlyList<AppointmentEventReadModel>>.Ok(await queries.GetRangeAsync(
            ClinicCalendar.ToUtcTicks(q.Start, 0, tz), ClinicCalendar.ToUtcTicks(q.End.AddDays(1), 0, tz), q.DoctorId, ct));
    }
}

[RequiresPermission(PermissionCodes.AppointmentsRead)]
public sealed record AppointmentDayCountsQuery(DateOnly Start, DateOnly End) : IQuery<IReadOnlyDictionary<DateOnly, int>>;
public sealed class AppointmentDayCountsQueryHandler(IAppointmentQueries queries, IClinicTimeZoneProvider zones)
    : IQueryHandler<AppointmentDayCountsQuery, IReadOnlyDictionary<DateOnly, int>>
{
    public async Task<Result<IReadOnlyDictionary<DateOnly, int>>> HandleAsync(AppointmentDayCountsQuery q, QueryContext context, CancellationToken ct)
    {
        if (SchedulingQueryWork.InvalidRange(q.Start, q.End))
            return Result<IReadOnlyDictionary<DateOnly, int>>.Fail(ErrorCodes.AppointmentRangeTooWide);
        var tz = zones.GetClinicTimeZone();
        var raw = await queries.GetDayCountsRawAsync(ClinicCalendar.ToUtcTicks(q.Start, 0, tz),
            ClinicCalendar.ToUtcTicks(q.End.AddDays(1), 0, tz), ct);
        return Result<IReadOnlyDictionary<DateOnly, int>>.Ok(raw.GroupBy(r => ClinicCalendar.LocalDay(r.Key, tz))
            .ToDictionary(g => g.Key, g => g.Sum(v => v.Value)));
    }
}

[RequiresPermission(PermissionCodes.AppointmentsRead)]
public sealed record DoctorBusyCountQuery(string DoctorId, DateOnly LocalDay) : IQuery<int>;
public sealed class DoctorBusyCountQueryHandler(IAppointmentQueries queries, IClinicTimeZoneProvider zones)
    : IQueryHandler<DoctorBusyCountQuery, int>
{
    public async Task<Result<int>> HandleAsync(DoctorBusyCountQuery q, QueryContext context, CancellationToken ct)
    {
        var (start, end) = ClinicCalendar.DayRangeUtc(q.LocalDay, zones.GetClinicTimeZone());
        return Result<int>.Ok(await queries.GetDoctorBusyCountAsync(q.DoctorId, start, end, ct));
    }
}

[RequiresPermission(PermissionCodes.AppointmentsRead)]
public sealed record AppointmentDetailQuery(string AppointmentId) : IQuery<AppointmentDetailReadModel?>;
public sealed class AppointmentDetailQueryHandler(IAppointmentQueries queries)
    : IQueryHandler<AppointmentDetailQuery, AppointmentDetailReadModel?>
{
    public async Task<Result<AppointmentDetailReadModel?>> HandleAsync(AppointmentDetailQuery q, QueryContext context, CancellationToken ct) =>
        Result<AppointmentDetailReadModel?>.Ok(await queries.GetDetailAsync(q.AppointmentId, ct));
}

[RequiresPermission(PermissionCodes.AppointmentsRead)]
public sealed record AppointmentQueueQuery : IQuery<IReadOnlyList<AppointmentEventReadModel>>;
public sealed class AppointmentQueueQueryHandler(IAppointmentQueries queries, IClinicTimeZoneProvider zones, IClock clock)
    : IQueryHandler<AppointmentQueueQuery, IReadOnlyList<AppointmentEventReadModel>>
{
    public async Task<Result<IReadOnlyList<AppointmentEventReadModel>>> HandleAsync(AppointmentQueueQuery q, QueryContext context, CancellationToken ct)
    {
        var tz = zones.GetClinicTimeZone();
        var day = ClinicCalendar.LocalDay(clock.UtcNow.UtcTicks, tz);
        var (start, end) = ClinicCalendar.DayRangeUtc(day, tz);
        return Result<IReadOnlyList<AppointmentEventReadModel>>.Ok(await queries.GetQueueAsync(start, end, ct));
    }
}

[RequiresPermission(PermissionCodes.AppointmentsRead)]
public sealed record AppointmentHistoryQuery : IQuery<IReadOnlyList<AppointmentEventReadModel>>;
public sealed class AppointmentHistoryQueryHandler(IAppointmentQueries queries)
    : IQueryHandler<AppointmentHistoryQuery, IReadOnlyList<AppointmentEventReadModel>>
{
    public async Task<Result<IReadOnlyList<AppointmentEventReadModel>>> HandleAsync(AppointmentHistoryQuery q, QueryContext context, CancellationToken ct) =>
        Result<IReadOnlyList<AppointmentEventReadModel>>.Ok(await queries.GetHistoryAsync(ct));
}

[RequiresPermission(PermissionCodes.AppointmentsRead)]
public sealed record AvailableSlotsQuery(string DoctorId, DateOnly LocalDay, int DurationMinutes) : IQuery<IReadOnlyList<TimeSlotReadModel>>;
public sealed class AvailableSlotsQueryHandler(IAppointmentQueries queries, IClinicTimeZoneProvider zones)
    : IQueryHandler<AvailableSlotsQuery, IReadOnlyList<TimeSlotReadModel>>
{
    public async Task<Result<IReadOnlyList<TimeSlotReadModel>>> HandleAsync(AvailableSlotsQuery q, QueryContext context, CancellationToken ct)
    {
        var hours = await queries.GetDoctorWorkingHoursAsync(q.DoctorId, ct);
        if (hours is null) return Result<IReadOnlyList<TimeSlotReadModel>>.Ok([]);
        var tz = zones.GetClinicTimeZone();
        var (start, end) = ClinicCalendar.DayRangeUtc(q.LocalDay, tz);
        var busy = await queries.GetDoctorSlotsAsync(q.DoctorId, start, end, null, ct);
        return Result<IReadOnlyList<TimeSlotReadModel>>.Ok(
            SchedulingQueryWork.Available(q.LocalDay, q.DurationMinutes, hours.Value, busy, tz, q.DoctorId));
    }
}

[RequiresPermission(PermissionCodes.AppointmentsRead)]
public sealed record AppointmentDoctorsQuery : IQuery<IReadOnlyList<DoctorRosterReadModel>>;
public sealed class AppointmentDoctorsQueryHandler(IAppointmentQueries queries)
    : IQueryHandler<AppointmentDoctorsQuery, IReadOnlyList<DoctorRosterReadModel>>
{
    public async Task<Result<IReadOnlyList<DoctorRosterReadModel>>> HandleAsync(AppointmentDoctorsQuery q, QueryContext context, CancellationToken ct) =>
        Result<IReadOnlyList<DoctorRosterReadModel>>.Ok(await queries.GetDoctorsAsync(ct));
}

[RequiresPermission(PermissionCodes.AppointmentsRead)]
public sealed record BookingPatientsQuery : IQuery<IReadOnlyList<BookingPatientReadModel>>;
public sealed class BookingPatientsQueryHandler(IAppointmentQueries queries)
    : IQueryHandler<BookingPatientsQuery, IReadOnlyList<BookingPatientReadModel>>
{
    public async Task<Result<IReadOnlyList<BookingPatientReadModel>>> HandleAsync(BookingPatientsQuery q, QueryContext context, CancellationToken ct) =>
        Result<IReadOnlyList<BookingPatientReadModel>>.Ok(await queries.GetPatientsAsync(ct));
}
