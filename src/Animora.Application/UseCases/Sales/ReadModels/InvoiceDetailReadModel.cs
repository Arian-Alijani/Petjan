using Animora.Application.Abstractions;
using Animora.Domain.Sales;

namespace Animora.Application.UseCases.Sales.ReadModels;

public sealed record InvoiceLineReadModel(string LineId, InvoiceLineKind Kind, string? ProductId,
    string? VariantId, string? CatalogItemId, string Title, decimal Quantity, long UnitPriceMinor,
    decimal DiscountPercent, decimal TaxPercent)
{
    /// <summary><c>quantity × unit price</c> before any discount, through the domain's single rounding boundary.</summary>
    public long GrossMinor => Gross().Minor;

    /// <summary>The line's own discount amount (X26/X39: computed by the domain rules, never by the UI).</summary>
    public long DiscountMinor => Gross().ApplyPercent(DiscountPercent).Minor;

    /// <summary>Gross minus the line discount, before tax — what the screens show as the line subtotal.</summary>
    public long SubtotalMinor => GrossMinor - DiscountMinor;

    private Animora.Domain.ValueObjects.Money Gross() =>
        Animora.Domain.ValueObjects.Money.Irt(UnitPriceMinor).ApplyPercent(Quantity * 100m);
}

public sealed record InvoicePaymentReadModel(string Id, PaymentMethod Method, long AmountMinor,
    DateTimeOffset ReceivedUtc, string? Note);

public sealed record InvoiceAdjustmentReadModel(string Id, AdjustmentKind Kind, long AmountMinor,
    string Reason, DateTimeOffset CreatedUtc);

public sealed record InvoiceDetailReadModel(InvoiceSummaryReadModel Summary, string? GuestName,
    string? InsurerId, decimal InsurancePercent, long SubtotalMinor, long DiscountMinor,
    long TaxMinor, long InsuranceMinor, long PaidMinor, DiscountKind DiscountKind,
    decimal DiscountValue, IReadOnlyList<InvoiceLineReadModel> Lines,
    IReadOnlyList<InvoicePaymentReadModel> Payments, IReadOnlyList<InvoiceAdjustmentReadModel> Adjustments,
    SettlementStatus SettlementStatus, string? LastSettlementErrorCode, string? ResponsibleRole, bool IsReadOnly = false);
