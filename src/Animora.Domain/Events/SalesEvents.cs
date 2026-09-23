using Animora.Domain.Sales;

namespace Animora.Domain.Events;

/// <summary>
/// An invoice was issued and is now immutable (A-RULE 002). Carries the totals in minor units so a
/// revenue projection, the ledger writer (A08) and the settlement pipeline (B02) need no second read.
/// </summary>
/// <param name="EventId">ULID of the event.</param>
/// <param name="TenantId">Owning tenant.</param>
/// <param name="OccurredAtUtc">When it was raised (informational — A-RULE 011).</param>
/// <param name="InvoiceId">The posted invoice.</param>
/// <param name="OwnerId">The party billed; <see langword="null"/> for a guest sale.</param>
/// <param name="GrandTotalMinor">What the payer owes, in minor units.</param>
/// <param name="Currency">ISO-4217 code of the amount.</param>
/// <param name="VisitId">The visit billed, when there is one.</param>
/// <param name="BoardingStayId">The stay billed, when there is one.</param>
public sealed record InvoicePostedEvent(
    string EventId,
    string TenantId,
    DateTimeOffset OccurredAtUtc,
    string InvoiceId,
    string? OwnerId,
    long GrandTotalMinor,
    string Currency,
    string? VisitId,
    string? BoardingStayId)
    : DomainEvent(EventId, TenantId, OccurredAtUtc);

/// <summary>
/// Money moved against an invoice. The amount is signed: positive for a receipt, negative for the
/// compensating entry of a refund (A-RULE 002/003).
/// </summary>
/// <param name="EventId">ULID of the event.</param>
/// <param name="TenantId">Owning tenant.</param>
/// <param name="OccurredAtUtc">When it was raised (informational — A-RULE 011).</param>
/// <param name="PaymentId">The append-only payment entry.</param>
/// <param name="InvoiceId">The invoice paid.</param>
/// <param name="AmountMinor">The signed amount, in minor units.</param>
/// <param name="Currency">ISO-4217 code of the amount.</param>
/// <param name="Method">How the money moved.</param>
public sealed record PaymentRegisteredEvent(
    string EventId,
    string TenantId,
    DateTimeOffset OccurredAtUtc,
    string PaymentId,
    string InvoiceId,
    long AmountMinor,
    string Currency,
    PaymentMethod Method)
    : DomainEvent(EventId, TenantId, OccurredAtUtc);

/// <summary>
/// A posted invoice was refunded through a compensating adjustment — never through an edit.
/// </summary>
/// <param name="EventId">ULID of the event.</param>
/// <param name="TenantId">Owning tenant.</param>
/// <param name="OccurredAtUtc">When it was raised (informational — A-RULE 011).</param>
/// <param name="InvoiceId">The refunded invoice.</param>
/// <param name="AdjustmentId">The adjustment that carries the refund.</param>
/// <param name="AmountMinor">The refunded amount, in minor units.</param>
/// <param name="Currency">ISO-4217 code of the amount.</param>
public sealed record InvoiceRefundedEvent(
    string EventId,
    string TenantId,
    DateTimeOffset OccurredAtUtc,
    string InvoiceId,
    string AdjustmentId,
    long AmountMinor,
    string Currency)
    : DomainEvent(EventId, TenantId, OccurredAtUtc);

/// <summary>
/// An invoice was cancelled before any money moved. A paid invoice is never voided — it is refunded.
/// </summary>
/// <param name="EventId">ULID of the event.</param>
/// <param name="TenantId">Owning tenant.</param>
/// <param name="OccurredAtUtc">When it was raised (informational — A-RULE 011).</param>
/// <param name="InvoiceId">The voided invoice.</param>
/// <param name="Reason">Why it was voided; kept for the audit trail.</param>
public sealed record InvoiceVoidedEvent(
    string EventId,
    string TenantId,
    DateTimeOffset OccurredAtUtc,
    string InvoiceId,
    string Reason)
    : DomainEvent(EventId, TenantId, OccurredAtUtc);

/// <summary>A cashier opened a drawer with its float.</summary>
/// <param name="EventId">ULID of the event.</param>
/// <param name="TenantId">Owning tenant.</param>
/// <param name="OccurredAtUtc">When it was raised (informational — A-RULE 011).</param>
/// <param name="SessionId">The opened session.</param>
/// <param name="CashierUserId">The cashier who owns the drawer.</param>
public sealed record CashSessionOpenedEvent(
    string EventId,
    string TenantId,
    DateTimeOffset OccurredAtUtc,
    string SessionId,
    string CashierUserId)
    : DomainEvent(EventId, TenantId, OccurredAtUtc);

/// <summary>
/// A drawer was closed. The variance is carried so a supervisor queue can pick up an over/short
/// drawer without re-reading the session.
/// </summary>
/// <param name="EventId">ULID of the event.</param>
/// <param name="TenantId">Owning tenant.</param>
/// <param name="OccurredAtUtc">When it was raised (informational — A-RULE 011).</param>
/// <param name="SessionId">The closed session.</param>
/// <param name="VarianceMinor">Declared minus expected, in minor units; positive is over, negative short.</param>
/// <param name="Currency">ISO-4217 code of the amount.</param>
public sealed record CashSessionClosedEvent(
    string EventId,
    string TenantId,
    DateTimeOffset OccurredAtUtc,
    string SessionId,
    long VarianceMinor,
    string Currency)
    : DomainEvent(EventId, TenantId, OccurredAtUtc);
