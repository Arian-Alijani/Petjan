using System.Globalization;
using Animora.Application.Abstractions;
using Animora.Application.Ports;
using Animora.Application.Security;
using Animora.Application.Time;
using Animora.Contracts.Vaccination;
using Animora.Domain.Clinical;
using Animora.Domain.Identity;
using Animora.Domain.Results;

namespace Animora.Application.UseCases.Vaccination;

[RequiresPermission(PermissionCodes.VaccinationRead)]
public sealed record VaccinationDueQuery(DateOnly LocalDay) : IQuery<IReadOnlyList<VaccinationDueRow>>;

public sealed class VaccinationDueQueryHandler(IVaccinationQueries queries)
    : IQueryHandler<VaccinationDueQuery, IReadOnlyList<VaccinationDueRow>>
{
    public async Task<Result<IReadOnlyList<VaccinationDueRow>>> HandleAsync(VaccinationDueQuery query,
        QueryContext context, CancellationToken ct) =>
        Result<IReadOnlyList<VaccinationDueRow>>.Ok(await queries.GetDueAsync(query.LocalDay, ct));
}

/// <summary>
/// One vaccine of the clinic's «انبار واکسن» stock catalog. Only the clinic's own items are listed: the built-in
/// <see cref="VaccineSchedule"/> protocols stay a domain dose-schedule source (and keep their codes reserved), but are
/// no longer surfaced as fixed stock rows.
/// </summary>
public sealed record VaccineCatalogReadModel(string Code, string Name, int TotalDoses, int BoosterDays,
    string? Protects = null, int Stock = 0, int MinStock = 0, bool IsCustom = false);

[RequiresPermission(PermissionCodes.VaccinationRead)]
public sealed record VaccinationCatalogQuery : IQuery<IReadOnlyList<VaccineCatalogReadModel>>;

public sealed class VaccinationCatalogQueryHandler(ITreatmentPlanQueries custom)
    : IQueryHandler<VaccinationCatalogQuery, IReadOnlyList<VaccineCatalogReadModel>>
{
    public async Task<Result<IReadOnlyList<VaccineCatalogReadModel>>> HandleAsync(VaccinationCatalogQuery query,
        QueryContext context, CancellationToken ct)
    {
        var own = (await custom.GetCustomCatalogAsync(ct)).Select(r => new VaccineCatalogReadModel(r.Code, r.Name,
            r.TotalDoses, r.BoosterDays, r.Protects, r.Stock, r.MinStock, IsCustom: true));
        return Result<IReadOnlyList<VaccineCatalogReadModel>>.Ok(own.ToArray());
    }
}

[RequiresPermission(PermissionCodes.VaccinationRead)]
public sealed record SmsTemplatesQuery : IQuery<IReadOnlyList<SmsTemplateReadModel>>;

public sealed class SmsTemplatesQueryHandler(IVaccinationQueries queries)
    : IQueryHandler<SmsTemplatesQuery, IReadOnlyList<SmsTemplateReadModel>>
{
    public async Task<Result<IReadOnlyList<SmsTemplateReadModel>>> HandleAsync(SmsTemplatesQuery query,
        QueryContext context, CancellationToken ct) =>
        Result<IReadOnlyList<SmsTemplateReadModel>>.Ok(await queries.GetTemplatesAsync(ct));
}

/// <summary>
/// Recurring-vaccine boosters that fall due in a local-day window — the Appointments calendar projects these as
/// read-time «booster due» entries (the vaccination record stays the single source of truth).
/// </summary>
[RequiresPermission(PermissionCodes.AppointmentsRead)]
public sealed record VaccinationBoostersRangeQuery(DateOnly Start, DateOnly End)
    : IQuery<IReadOnlyList<VaccinationBoosterRow>>;

public sealed class VaccinationBoostersRangeQueryHandler(
    IVaccinationQueries queries, IClinicTimeZoneProvider zones)
    : IQueryHandler<VaccinationBoostersRangeQuery, IReadOnlyList<VaccinationBoosterRow>>
{
    private const int MaxRows = 500;

    public async Task<Result<IReadOnlyList<VaccinationBoosterRow>>> HandleAsync(
        VaccinationBoostersRangeQuery query, QueryContext context, CancellationToken ct)
    {
        if (query.End.DayNumber < query.Start.DayNumber || query.End.DayNumber - query.Start.DayNumber > 62)
            return Result<IReadOnlyList<VaccinationBoosterRow>>.Fail(ErrorCodes.AppointmentRangeTooWide);
        var tz = zones.GetClinicTimeZone();
        var from = new DateTimeOffset(ClinicCalendar.ToUtcTicks(query.Start, 0, tz), TimeSpan.Zero);
        var to = new DateTimeOffset(ClinicCalendar.ToUtcTicks(query.End.AddDays(1), 0, tz), TimeSpan.Zero);
        return Result<IReadOnlyList<VaccinationBoosterRow>>.Ok(
            await queries.GetPendingBoostersAsync(from, to, MaxRows, ct));
    }
}

/// <summary>The reminder SMS messages recorded for one owner's pets, newest first (bounded).</summary>
[RequiresPermission(PermissionCodes.VaccinationRead)]
public sealed record OwnerSmsRemindersQuery(string OwnerId, int Take = 50)
    : IQuery<IReadOnlyList<OwnerSmsReminderRow>>;

public sealed class OwnerSmsRemindersQueryHandler(IVaccinationQueries queries)
    : IQueryHandler<OwnerSmsRemindersQuery, IReadOnlyList<OwnerSmsReminderRow>>
{
    public async Task<Result<IReadOnlyList<OwnerSmsReminderRow>>> HandleAsync(OwnerSmsRemindersQuery query,
        QueryContext context, CancellationToken ct) =>
        Result<IReadOnlyList<OwnerSmsReminderRow>>.Ok(await queries.GetOwnerSmsRemindersAsync(
            query.OwnerId, Math.Clamp(query.Take, 1, 200), ct));
}

public sealed record VaccinationHistoryPageReadModel(IReadOnlyList<VaccinationHistoryRow> Items, int TotalCount);

[RequiresPermission(PermissionCodes.VaccinationRead)]
public sealed record VaccinationHistoryQuery(VaccinationHistoryFilter Filter)
    : IQuery<VaccinationHistoryPageReadModel>;

public sealed class VaccinationHistoryQueryHandler(IVaccinationQueries queries)
    : IQueryHandler<VaccinationHistoryQuery, VaccinationHistoryPageReadModel>
{
    public async Task<Result<VaccinationHistoryPageReadModel>> HandleAsync(VaccinationHistoryQuery query,
        QueryContext context, CancellationToken ct)
    {
        if (!TryJalaliDate(query.Filter.FromDate, out var from) ||
            !TryJalaliDate(query.Filter.ToDate, out var to))
            return Result<VaccinationHistoryPageReadModel>.Fail(ErrorCodes.ValidationFailed);
        IReadOnlyCollection<string>? titles = query.Filter.Kinds.Count == 0 ? null :
            query.Filter.Kinds.SelectMany(VaccinationHistoryTitles.For).Distinct().ToArray();
        var (items, total) = await queries.GetHistoryAsync(from, to?.AddDays(1),
            query.Filter.Search, query.Filter.Sort == VaccinationHistorySort.Oldest,
            titles, query.Filter.Page, query.Filter.PageSize, ct);
        return Result<VaccinationHistoryPageReadModel>.Ok(new(items, total));
    }

    private static bool TryJalaliDate(string? value, out DateTimeOffset? date)
    {
        date = null;
        if (string.IsNullOrWhiteSpace(value)) return true;
        var parts = value.Split('/');
        if (parts.Length != 3 || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var y)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var m)
            || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var d)) return false;
        try
        {
            var utcDate = new PersianCalendar().ToDateTime(y, m, d, 0, 0, 0, 0);
            date = new DateTimeOffset(utcDate, TimeSpan.Zero);
            return true;
        }
        catch (ArgumentOutOfRangeException) { return false; }
    }
}
