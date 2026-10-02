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

public enum AppointmentHistoryStatus
{
    All,
    Completed,
    NoShow,
    Cancelled,
}

/// <summary>
/// Parameterized archive read (A01): local-day range, status, doctor, type, invoice bucket and a text search,
/// all resolved in SQL, paged, with aggregates computed over the whole filtered scope. History never reaches
/// past the end of the clinic's current day. <see cref="DoctorId"/> null means every doctor.
/// </summary>
[RequiresPermission(PermissionCodes.AppointmentsRead)]
public sealed record AppointmentHistoryQuery(
    DateOnly? From = null, DateOnly? To = null,
    AppointmentHistoryStatus Status = AppointmentHistoryStatus.All,
    string? DoctorId = null, string? TypeCode = null,
    HistoryInvoiceFilter Invoice = HistoryInvoiceFilter.All,
    string? Search = null, int Page = 0, int PageSize = AppointmentHistoryQuery.DefaultPageSize)
    : IQuery<AppointmentHistoryPageReadModel>
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 100;
    public const int MaxSearchTerms = 5;
    public const int MaxSearchLength = 64;
}

public sealed class AppointmentHistoryQueryHandler(IAppointmentQueries queries, IClinicTimeZoneProvider zones, IClock clock)
    : IQueryHandler<AppointmentHistoryQuery, AppointmentHistoryPageReadModel>
{
    private static readonly AppointmentHistoryPageReadModel Empty = new([], new(0, 0, 0, 0, 0, 0), false);

    public async Task<Result<AppointmentHistoryPageReadModel>> HandleAsync(
        AppointmentHistoryQuery q, QueryContext context, CancellationToken ct)
    {
        var tz = zones.GetClinicTimeZone();
        var today = ClinicCalendar.LocalDay(clock.UtcNow.UtcTicks, tz);
        var to = q.To is { } requested && requested < today ? requested : today;
        if (q.From is { } from && from > to) return Result<AppointmentHistoryPageReadModel>.Ok(Empty);

        var size = Math.Clamp(q.PageSize, 1, AppointmentHistoryQuery.MaxPageSize);
        var terms = SearchTerms(q.Search);
        var criteria = new AppointmentHistoryCriteria(
            q.From is { } f ? ClinicCalendar.ToUtcTicks(f, 0, tz) : null,
            ClinicCalendar.ToUtcTicks(to.AddDays(1), 0, tz),
            q.Status switch
            {
                AppointmentHistoryStatus.Completed => AppointmentState.Completed,
                AppointmentHistoryStatus.NoShow => AppointmentState.NoShow,
                AppointmentHistoryStatus.Cancelled => AppointmentState.Cancelled,
                _ => null,
            },
            string.IsNullOrWhiteSpace(q.DoctorId) ? null : q.DoctorId,
            string.IsNullOrWhiteSpace(q.TypeCode) ? null : q.TypeCode,
            q.Invoice, terms, Math.Max(0, q.Page) * size, size);
        return Result<AppointmentHistoryPageReadModel>.Ok(await queries.GetHistoryPageAsync(criteria, ct));
    }

    /// <summary>Normalized, bounded search terms (length and count capped so a pasted blob cannot fan out).</summary>
    internal static IReadOnlyList<string> SearchTerms(string? search)
    {
        var normalized = Animora.Application.Text.PersianText.Normalize(search);
        if (normalized.Length > AppointmentHistoryQuery.MaxSearchLength)
            normalized = normalized[..AppointmentHistoryQuery.MaxSearchLength];
        return normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Take(AppointmentHistoryQuery.MaxSearchTerms).ToArray();
    }
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
public sealed record BookingPatientsQuery(
    string Search = "", string SpeciesKey = "all", int Limit = 21,
    string? PatientId = null, string? AppointmentId = null) : IQuery<IReadOnlyList<BookingPatientReadModel>>;
public sealed class BookingPatientsQueryHandler(IAppointmentQueries queries)
    : IQueryHandler<BookingPatientsQuery, IReadOnlyList<BookingPatientReadModel>>
{
    public async Task<Result<IReadOnlyList<BookingPatientReadModel>>> HandleAsync(BookingPatientsQuery q, QueryContext context, CancellationToken ct) =>
        Result<IReadOnlyList<BookingPatientReadModel>>.Ok(await queries.GetPatientsAsync(q.Search, q.SpeciesKey, Math.Clamp(q.Limit, 1, 21), q.PatientId, q.AppointmentId, ct));
}
