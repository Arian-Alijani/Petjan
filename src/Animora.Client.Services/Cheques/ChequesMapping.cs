using System.Globalization;
using Animora.Application.Ports;
using Animora.Application.Time;
using Animora.Application.UseCases.Finance.Cheques;
using Animora.Contracts.Cheques;
using Animora.Domain.Finance;

namespace Animora.Client.Services.Cheques;

/// <summary>Read-model → Contracts mapping for the cheques screen (pure; Jalali dates in the clinic zone).</summary>
internal static class ChequesMapping
{
    /// <summary>The lifecycle action → the state it moves to; the domain machine decides whether it is allowed from the current state.</summary>
    private static readonly (ChequeActionDto Action, ChequeState Target)[] ActionTargets =
    [
        (ChequeActionDto.MoveToPortfolio, ChequeState.InPortfolio),
        (ChequeActionDto.Deposit, ChequeState.Deposited),
        (ChequeActionDto.MarkCleared, ChequeState.Cleared),
        (ChequeActionDto.MarkBounced, ChequeState.Bounced),
        (ChequeActionDto.Return, ChequeState.Returned),
        (ChequeActionDto.Cancel, ChequeState.Cancelled),
        (ChequeActionDto.SettleInCash, ChequeState.Settled),
    ];

    internal static ChequeItem ToContract(this ChequeRow row, TimeZoneInfo zone, DateOnly today)
    {
        var actions = new List<ChequeActionDto>(3);
        foreach (var (action, target) in ActionTargets)
        {
            if (Cheque.IsTransitionAllowed(row.State, target)) actions.Add(action);
        }

        var dueDay = ClinicCalendar.LocalDay(row.DueUtc.UtcTicks, zone);
        return new ChequeItem(row.Id, row.Direction.ToContract(), row.Number, row.Sayad, row.BankKey, row.Branch,
            row.Account, row.Party, row.AmountMinor, Jalali(row.IssuedUtc, zone), Jalali(row.DueUtc, zone),
            dueDay.DayNumber - today.DayNumber, row.State.ToContract(), row.Note, actions,
            row.State == ChequeState.Registered);
    }

    internal static ChequeHistoryEntry ToContract(this ChequeHistoryRow row, TimeZoneInfo zone) =>
        new(row.From.ToContract(), row.To.ToContract(), Jalali(row.OccurredUtc, zone), Time(row.OccurredUtc, zone),
            row.ActorName, row.Reason);

    internal static ChequeSummary ToContract(this ChequeSummaryRow row) =>
        new(row.InboundOpenMinor, row.InboundOpenCount, row.OutboundOpenMinor, row.OutboundOpenCount,
            row.BouncedMinor, row.BouncedCount);

    internal static ChequeParty ToContract(this ChequePartyRow row) => new(row.Id, row.Name, row.Phone);

    internal static ChequeDirectionDto ToContract(this ChequeDirection value) =>
        value == ChequeDirection.Inbound ? ChequeDirectionDto.Inbound : ChequeDirectionDto.Outbound;

    internal static ChequeDirection ToDomain(this ChequeDirectionDto value) =>
        value == ChequeDirectionDto.Inbound ? ChequeDirection.Inbound : ChequeDirection.Outbound;

    internal static ChequeStateDto ToContract(this ChequeState value) => (ChequeStateDto)(int)value;

    internal static ChequeListScope ToApplication(this ChequeScopeDto value) => value switch
    {
        ChequeScopeDto.Open => ChequeListScope.Open,
        ChequeScopeDto.Attention => ChequeListScope.Attention,
        ChequeScopeDto.Closed => ChequeListScope.Closed,
        _ => ChequeListScope.All,
    };

    internal static ChequeTransition ToApplication(this ChequeActionDto value) => value switch
    {
        ChequeActionDto.MoveToPortfolio => ChequeTransition.MoveToPortfolio,
        ChequeActionDto.Deposit => ChequeTransition.Deposit,
        ChequeActionDto.MarkCleared => ChequeTransition.MarkCleared,
        ChequeActionDto.MarkBounced => ChequeTransition.MarkBounced,
        ChequeActionDto.Return => ChequeTransition.Return,
        ChequeActionDto.Cancel => ChequeTransition.Cancel,
        _ => ChequeTransition.SettleInCash,
    };

    /// <summary>
    /// Parses an ASCII Jalali <c>yyyy/MM/dd</c> day into the UTC instant of that clinic-local day's start, or
    /// <see langword="null"/> when empty/invalid. Mid-day anchoring is avoided so «due today» stays the same local day.
    /// </summary>
    internal static DateTimeOffset? ParseJalali(string? value, TimeZoneInfo zone) =>
        ClinicDayWindow.ParseJalali(value ?? string.Empty, zone, endOfDay: false);

    private static string Jalali(DateTimeOffset utc, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(utc, zone).DateTime;
        var calendar = new PersianCalendar();
        return $"{calendar.GetYear(local):0000}/{calendar.GetMonth(local):00}/{calendar.GetDayOfMonth(local):00}";
    }

    private static string Time(DateTimeOffset utc, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTime(utc, zone).ToString("HH:mm", CultureInfo.InvariantCulture);
}
