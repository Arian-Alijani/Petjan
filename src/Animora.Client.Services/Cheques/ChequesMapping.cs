using System.Globalization;
using Animora.Application.Ports;
using Animora.Contracts.Cheques;
using Animora.Domain.Finance;
using ContractCheque = Animora.Contracts.Cheques.Cheque;
using ContractDirection = Animora.Contracts.Cheques.ChequeDirection;
using DomainDirection = Animora.Domain.Finance.ChequeDirection;

namespace Animora.Client.Services.Cheques;

internal static class ChequesMapping
{
    private static readonly PersianCalendar Calendar = new();
    private static readonly IReadOnlyDictionary<ChequeState, ChequeStatus> States =
        new Dictionary<ChequeState, ChequeStatus>
        {
            [ChequeState.Registered] = ChequeStatus.InHand,
            [ChequeState.InPortfolio] = ChequeStatus.InHand,
            [ChequeState.Deposited] = ChequeStatus.AtBank,
            [ChequeState.Cleared] = ChequeStatus.Cleared,
            [ChequeState.Bounced] = ChequeStatus.Bounced,
            [ChequeState.Returned] = ChequeStatus.Spent,
            [ChequeState.Cancelled] = ChequeStatus.Void,
            [ChequeState.Settled] = ChequeStatus.Cleared,
        };

    internal static ChequeStatus Status(ChequeState state) => States[state];

    internal static ChequeState Target(ChequeStatus status) => status switch
    {
        ChequeStatus.InHand => ChequeState.InPortfolio,
        ChequeStatus.AtBank => ChequeState.Deposited,
        ChequeStatus.Cleared => ChequeState.Cleared,
        ChequeStatus.Bounced => ChequeState.Bounced,
        ChequeStatus.Spent => ChequeState.Returned,
        ChequeStatus.Void => ChequeState.Cancelled,
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    internal static string Date(DateTimeOffset utc) =>
        $"{Calendar.GetYear(utc.UtcDateTime):0000}/{Calendar.GetMonth(utc.UtcDateTime):00}/{Calendar.GetDayOfMonth(utc.UtcDateTime):00}";

    internal static DateTimeOffset ParseDate(string jalali)
    {
        var parts = jalali.Split('/');
        if (parts.Length != 3 || !int.TryParse(parts[0], out var year) ||
            !int.TryParse(parts[1], out var month) || !int.TryParse(parts[2], out var day))
            throw new FormatException("A Jalali yyyy/MM/dd date is required.");
        return new DateTimeOffset(Calendar.ToDateTime(year, month, day, 12, 0, 0, 0), TimeSpan.Zero);
    }

    internal static ContractCheque ToContract(this ChequeRow row)
    {
        var bank = BankDirectory.All.FirstOrDefault(b => b.Name == row.BankKey || b.Key == row.BankKey);
        return new ContractCheque(row.Id, row.Direction == DomainDirection.Inbound ? ContractDirection.In : ContractDirection.Out,
            row.Number, row.Sayad ?? string.Empty, bank?.Key ?? row.BankKey ?? string.Empty,
            row.Branch ?? string.Empty, row.Party, string.Empty, string.Empty, row.AmountMinor,
            Date(row.IssuedUtc), Date(row.DueUtc), Status(row.State), string.Empty,
            row.InvoiceId ?? string.Empty, row.Account ?? string.Empty, row.Note ?? string.Empty);
    }
}
