using Animora.Application.Abstractions;
using Animora.Domain.Sales;

namespace Animora.Application.UseCases.Sales.ReadModels;

public sealed record InvoiceLineReadModel(string LineId, InvoiceLineKind Kind, string? ProductId,
    string? VariantId, string? CatalogItemId, string Title, decimal Quantity, long UnitPriceMinor,
    decimal DiscountPercent, decimal TaxPercent);

public sealed record InvoicePaymentReadModel(string Id, PaymentMethod Method, long AmountMinor,
    DateTimeOffset ReceivedUtc, string? Note);

public sealed record InvoiceAdjustmentReadModel(string Id, AdjustmentKind Kind, long AmountMinor,
    string Reason, DateTimeOffset CreatedUtc);

public sealed record InvoiceDetailReadModel(InvoiceSummaryReadModel Summary, string? GuestName,
    string? InsurerId, decimal InsurancePercent, long SubtotalMinor, long DiscountMinor,
    long TaxMinor, long InsuranceMinor, long PaidMinor, DiscountKind DiscountKind,
    decimal DiscountValue, IReadOnlyList<InvoiceLineReadModel> Lines,
    IReadOnlyList<InvoicePaymentReadModel> Payments, IReadOnlyList<InvoiceAdjustmentReadModel> Adjustments,
    SettlementStatus SettlementStatus, string? LastSettlementErrorCode, string? ResponsibleRole);
