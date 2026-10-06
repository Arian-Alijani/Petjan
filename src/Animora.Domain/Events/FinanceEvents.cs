using Animora.Domain.Finance;
using Animora.Domain.Sales;

namespace Animora.Domain.Events;

/// <summary>
/// A balanced double entry was appended to the general ledger. Carries the debit total in minor
/// units so a finance projection (C09) needs no second read of the entry's lines.
/// </summary>
/// <param name="EventId">ULID of the event.</param>
/// <param name="TenantId">Owning tenant.</param>
/// <param name="OccurredAtUtc">When it was raised (informational — A-RULE 011).</param>
/// <param name="EntryId">The appended entry; append-only forever (A-RULE 002 / R-B22).</param>
/// <param name="PeriodId">The accounting period the entry falls in.</param>
/// <param name="Source">What caused the entry.</param>
/// <param name="TotalDebitMinor">Σ of the debit lines — equal to the credit total by construction.</param>
/// <param name="Currency">ISO-4217 code of the amount.</param>
public sealed record JournalEntryPostedEvent(
    string EventId,
    string TenantId,
    DateTimeOffset OccurredAtUtc,
    string EntryId,
    string PeriodId,
    JournalSource Source,
    long TotalDebitMinor,
    string Currency)
    : DomainEvent(EventId, TenantId, OccurredAtUtc);

/// <summary>
/// A cheque moved to a new state. Raised by <b>every</b> accepted transition, alongside the
/// append-only <see cref="ChequeEvent"/> written into the cheque's history.
/// </summary>
/// <param name="EventId">ULID of the event.</param>
/// <param name="TenantId">Owning tenant.</param>
/// <param name="OccurredAtUtc">When it was raised (informational — A-RULE 011).</param>
/// <param name="ChequeId">The cheque that moved.</param>
/// <param name="FromState">The state it left.</param>
/// <param name="ToState">The state it entered.</param>
public sealed record ChequeStateChangedEvent(
    string EventId,
    string TenantId,
    DateTimeOffset OccurredAtUtc,
    string ChequeId,
    ChequeState FromState,
    ChequeState ToState)
    : DomainEvent(EventId, TenantId, OccurredAtUtc);

/// <summary>The bank honoured a cheque; the ledger recognises the cash movement.</summary>
/// <param name="EventId">ULID of the event.</param>
/// <param name="TenantId">Owning tenant.</param>
/// <param name="OccurredAtUtc">When it was raised (informational — A-RULE 011).</param>
/// <param name="ChequeId">The cleared cheque.</param>
/// <param name="Direction">Inbound (collected) or outbound (paid).</param>
/// <param name="AmountMinor">The face amount, in minor units.</param>
/// <param name="Currency">ISO-4217 code of the amount.</param>
public sealed record ChequeClearedEvent(
    string EventId,
    string TenantId,
    DateTimeOffset OccurredAtUtc,
    string ChequeId,
    ChequeDirection Direction,
    long AmountMinor,
    string Currency)
    : DomainEvent(EventId, TenantId, OccurredAtUtc);

/// <summary>The bank refused a cheque; the receivable (or payable) comes back.</summary>
/// <param name="EventId">ULID of the event.</param>
/// <param name="TenantId">Owning tenant.</param>
/// <param name="OccurredAtUtc">When it was raised (informational — A-RULE 011).</param>
/// <param name="ChequeId">The bounced cheque.</param>
/// <param name="Direction">Inbound (not collected) or outbound (not paid).</param>
/// <param name="AmountMinor">The face amount, in minor units.</param>
/// <param name="Currency">ISO-4217 code of the amount.</param>
/// <param name="Reason">Why the bank refused it; mandatory (A-RULE 003).</param>
public sealed record ChequeBouncedEvent(
    string EventId,
    string TenantId,
    DateTimeOffset OccurredAtUtc,
    string ChequeId,
    ChequeDirection Direction,
    long AmountMinor,
    string Currency,
    string Reason)
    : DomainEvent(EventId, TenantId, OccurredAtUtc);

/// <summary>An expense was posted and is now immutable (A-RULE 002).</summary>
/// <param name="EventId">ULID of the event.</param>
/// <param name="TenantId">Owning tenant.</param>
/// <param name="OccurredAtUtc">When it was raised (informational — A-RULE 011).</param>
/// <param name="ExpenseId">The posted expense.</param>
/// <param name="CategoryCode">Its category; resolves the debit account through <see cref="ExpenseCategories"/>.</param>
/// <param name="AmountMinor">The amount spent, in minor units.</param>
/// <param name="Currency">ISO-4217 code of the amount.</param>
/// <param name="SettledMethod">How it was settled; <see langword="null"/> leaves it payable.</param>
/// <param name="IsReversal">
/// <see langword="true"/> when the posted expense offsets an earlier one: the journal entry swaps sides and the
/// finance facts subtract the amount (A-RULE 003 — a correction is a compensating row, never an edit).
/// </param>
public sealed record ExpensePostedEvent(
    string EventId,
    string TenantId,
    DateTimeOffset OccurredAtUtc,
    string ExpenseId,
    string CategoryCode,
    long AmountMinor,
    string Currency,
    PaymentMethod? SettledMethod,
    bool IsReversal = false)
    : DomainEvent(EventId, TenantId, OccurredAtUtc);

/// <summary>
/// An accounting period was closed. There is no reopen: a later correction is an entry in the
/// next open period.
/// </summary>
/// <param name="EventId">ULID of the event.</param>
/// <param name="TenantId">Owning tenant.</param>
/// <param name="OccurredAtUtc">When it was raised (informational — A-RULE 011).</param>
/// <param name="PeriodId">The closed period's code.</param>
/// <param name="ClosingEntryId">The closing journal entry; mandatory for a close.</param>
public sealed record PeriodClosedEvent(
    string EventId,
    string TenantId,
    DateTimeOffset OccurredAtUtc,
    string PeriodId,
    string ClosingEntryId)
    : DomainEvent(EventId, TenantId, OccurredAtUtc);
