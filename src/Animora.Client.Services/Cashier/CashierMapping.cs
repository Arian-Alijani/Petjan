using System.Globalization;
using Animora.Application.UseCases.Sales.ReadModels;
using Animora.Contracts.Cashier;
using Animora.Domain.Sales;

namespace Animora.Client.Services.Cashier;

internal static class CashierMapping
{
    private static readonly PersianCalendar Calendar = new();

    internal static string Date(DateTimeOffset utc) =>
        $"{Calendar.GetYear(utc.UtcDateTime):0000}/{Calendar.GetMonth(utc.UtcDateTime):00}/{Calendar.GetDayOfMonth(utc.UtcDateTime):00}";

    internal static CashierInvoiceState State(InvoiceState state) => state switch
    {
        InvoiceState.Draft or InvoiceState.Posted => CashierInvoiceState.Draft,
        InvoiceState.PartiallyPaid => CashierInvoiceState.Partial,
        InvoiceState.Paid => CashierInvoiceState.Settled,
        InvoiceState.Void => CashierInvoiceState.Void,
        InvoiceState.Refunded => CashierInvoiceState.Refunded,
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };

    internal static string Label(CashierInvoiceState state) => state switch
    {
        CashierInvoiceState.Draft => "باز",
        CashierInvoiceState.Partial => "تسویه جزئی",
        CashierInvoiceState.Settled => "تسویه شده",
        CashierInvoiceState.Void => "باطل",
        _ => "عودت",
    };

    internal static CashierInvoice ToContract(this InvoiceSummaryReadModel row, long paid = 0)
    {
        var state = State(row.State);
        return new CashierInvoice(row.Id, state, Label(state), row.OwnerName, row.PetName,
            Date(row.IssuedUtc), row.TotalMinor, paid, row.DueMinor);
    }

    internal static CashierInvoiceDetail ToContract(this InvoiceDetailReadModel row)
    {
        var summary = row.Summary;
        var state = State(summary.State);
        var lines = row.Lines.Select((line, index) => new CashierInvoiceLine(index,
            line.Kind == InvoiceLineKind.Service ? CashierLineKind.Service : CashierLineKind.Item,
            line.Title, line.UnitPriceMinor, (int)line.Quantity, (int)line.DiscountPercent,
            line.SubtotalMinor, line.LineId)).ToArray();
        var payments = row.Payments.Select(p => new CashierPayment(
            p.Method switch
            {
                PaymentMethod.Cash => CashierPaymentMethod.Cash,
                PaymentMethod.Card => CashierPaymentMethod.Card,
                PaymentMethod.Cheque => CashierPaymentMethod.Cheque,
                _ => CashierPaymentMethod.Credit,
            }, p.Method.ToString(), string.Empty, p.Note ?? string.Empty,
            Date(p.ReceivedUtc), p.AmountMinor)).ToArray();
        return new CashierInvoiceDetail(summary.Id, state, Label(state), summary.OwnerName, summary.PetName,
            summary.Phone, Date(summary.IssuedUtc), summary.State == InvoiceState.Draft,
            row.InsurerId ?? string.Empty,
            row.DiscountKind == DiscountKind.Amount ? CashierDiscountKind.Amount : CashierDiscountKind.Percent,
            (int)row.DiscountValue, lines, payments, row.SubtotalMinor, row.DiscountMinor,
            row.SubtotalMinor - row.DiscountMinor, 0, row.TaxMinor, row.InsuranceMinor,
            summary.TotalMinor, row.PaidMinor, summary.DueMinor);
    }
}
