namespace Animora.Contracts.Wire.Sync;

/// <summary>
/// D04 — a bounded, read-only snapshot of a tenant's authoritative financial state
/// (<see cref="Invoices"/> + <see cref="Payments"/>) that changed at or after a timestamp cursor,
/// served by <c>GET /finance/ledger</c>. This is a SEPARATE channel from the command intake
/// (<c>/api/commands</c>) and the generic state feed (<c>/sync/*</c>): invoices/payments are
/// intentionally excluded from the feed (they are command-settled money), so this dedicated path
/// is the only way a peer can display a cross-device transaction. Rows are server-authored and are
/// NEVER fed back into the command pipeline — the client upserts them into a read-only cache keyed
/// by the stable id (invoice id == operation id, so an origin device's local row and the server's
/// authoritative row coincide and dedup).
/// </summary>
/// <param name="Invoices">Invoice summaries whose effective last-change time is ≥ the request cursor.</param>
/// <param name="Payments">Payments received at or after the request cursor (money movements in the window).</param>
/// <param name="NextCursorTicks">
/// The exclusive cursor to pass on the next poll: the maximum effective-change tick across the returned
/// rows (or the request cursor when nothing changed). UTC ticks; monotonic within a tenant.
/// </param>
/// <param name="HasMore">True when the bounded window was filled and another poll from <see cref="NextCursorTicks"/> may return more.</param>
public sealed record FinanceReadPage(
    IReadOnlyList<FinanceInvoiceRow> Invoices,
    IReadOnlyList<FinancePaymentRow> Payments,
    long NextCursorTicks,
    bool HasMore);

/// <summary>D04 — one server-authored invoice row for the peer finance cache (flat, display-shaped).</summary>
/// <param name="Id">Stable invoice id (== the creating operation id); the dedup/upsert key.</param>
/// <param name="Number">Authoritative invoice number allocated by the server.</param>
/// <param name="Kind">Invoice kind (<c>InvoiceKind</c> as an int, to keep the wire decoupled from domain enums).</param>
/// <param name="State">Invoice state (<c>InvoiceState</c> as an int: Draft/Posted/Void/...).</param>
/// <param name="OwnerId">Owner id, when the invoice is attributed to an owner.</param>
/// <param name="PartyName">Owner full name, or the guest name for a walk-in.</param>
/// <param name="PetName">Pet name, when known.</param>
/// <param name="IssuedUtc">When the invoice was issued.</param>
/// <param name="PostedUtc">When the invoice was posted (null while Draft).</param>
/// <param name="GrandTotalMinor">Grand total, minor units.</param>
/// <param name="PaidTotalMinor">Amount collected so far, minor units.</param>
/// <param name="DueTotalMinor">Outstanding balance, minor units.</param>
/// <param name="EffectiveTicks">The invoice's effective last-change tick (max of issued/posted/settled); drives the cursor.</param>
public sealed record FinanceInvoiceRow(
    string Id,
    string Number,
    int Kind,
    int State,
    string? OwnerId,
    string PartyName,
    string PetName,
    DateTimeOffset IssuedUtc,
    DateTimeOffset? PostedUtc,
    long GrandTotalMinor,
    long PaidTotalMinor,
    long DueTotalMinor,
    long EffectiveTicks);

/// <summary>D04 — one server-authored payment row for the peer finance cache (flat, display-shaped).</summary>
/// <param name="Id">Stable payment id; the dedup/upsert key.</param>
/// <param name="InvoiceId">The invoice this payment (or refund, when the amount is negative) settles.</param>
/// <param name="Method">Payment method (<c>PaymentMethod</c> as an int).</param>
/// <param name="AmountMinor">Signed amount in minor units (negative ⇒ refund).</param>
/// <param name="ReceivedUtc">When the money moved; also the payment's effective cursor tick.</param>
public sealed record FinancePaymentRow(
    string Id,
    string InvoiceId,
    int Method,
    long AmountMinor,
    DateTimeOffset ReceivedUtc);
