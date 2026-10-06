using System.Globalization;
using Animora.Application.Ports;
using Animora.Application.Time;
using Animora.Application.UseCases.Finance.ReadModels;
using Animora.Domain.Finance;
using Animora.Domain.Time;

namespace Animora.Application.UseCases.Finance;

/// <summary>
/// The Jalali calendar maths of fixed expenses. The Domain stays calendar-agnostic (§3.2): only this
/// Application helper turns "today" into a Jalali month, its length, the opaque <c>yyyyMM</c>
/// <see cref="RecurringExpensePayment.PeriodCode"/> and the due date, then calls the pure
/// <see cref="RecurringExpenseSchedule"/>.
/// </summary>
public static class RecurringExpensePeriods
{
    private static readonly PersianCalendar Jalali = new();

    /// <summary>The Jalali month containing a clinic-local day.</summary>
    public readonly record struct Period(string Code, int Year, int Month, int Day, int DaysInMonth);

    /// <summary>Resolves the current period from the clinic-local <paramref name="today"/>.</summary>
    public static Period Resolve(DateOnly today)
    {
        var at = today.ToDateTime(TimeOnly.MinValue);
        var year = Jalali.GetYear(at);
        var month = Jalali.GetMonth(at);
        return new($"{year:0000}{month:00}", year, month, Jalali.GetDayOfMonth(at), Jalali.GetDaysInMonth(year, month));
    }

    /// <summary>Gregorian date of the (clamped) due day inside <paramref name="period"/>.</summary>
    public static DateOnly DueDate(Period period, int dayOfMonth) =>
        DateOnly.FromDateTime(Jalali.ToDateTime(period.Year, period.Month,
            RecurringExpenseSchedule.EffectiveDueDay(dayOfMonth, period.DaysInMonth), 0, 0, 0, 0));

    /// <summary>Clinic-local today for the injected clock and zone.</summary>
    public static DateOnly Today(IClock clock, IClinicTimeZoneProvider zones) =>
        ClinicCalendar.LocalDay(clock.UtcNow.UtcTicks, zones.GetClinicTimeZone());

    /// <summary>Reads every template joined with the current period's payment and classifies each one.</summary>
    public static async Task<RecurringExpensesReadModel> BuildAsync(IRecurringExpenseQueries queries, IClock clock,
        IClinicTimeZoneProvider zones, CancellationToken ct)
    {
        var today = Today(clock, zones);
        var period = Resolve(today);
        var rows = await queries.GetForPeriodAsync(period.Code, ct);
        var items = rows.Select(r =>
        {
            var due = DueDate(period, r.DayOfMonth);
            var state = RecurringExpenseSchedule.Evaluate(r.IsActive, r.DayOfMonth, period.Day, period.DaysInMonth,
                r.PaidExpenseId is not null);
            return new RecurringExpenseReadModel(r.Id, r.CategoryCode, r.Title, r.AmountMinor, r.DayOfMonth, r.IsActive,
                r.SupplierId, r.SupplierName, r.Note, state, due, due.DayNumber - today.DayNumber, r.PaidExpenseId, r.PaidUtc);
        }).ToArray();
        return new(period.Code, today, items);
    }
}
