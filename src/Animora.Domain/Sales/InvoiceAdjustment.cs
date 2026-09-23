using Animora.Domain.ValueObjects;

namespace Animora.Domain.Sales;

/// <summary>Why an adjustment was written against a posted invoice.</summary>
public enum AdjustmentKind
{
    /// <summary>A credit note: part of the claim is cancelled without money moving.</summary>
    CreditNote = 0,

    /// <summary>Money was given back to the payer; pairs with a negative <see cref="Payment"/>.</summary>
    Refund = 1,

    /// <summary>An uncollectable remainder is written off.</summary>
    WriteOff = 2,

    /// <summary>A minor-unit correction, e.g. cash rounding at the counter.</summary>
    RoundingCorrection = 3,

    /// <summary>Goods came back; the line's economics are reversed.</summary>
    LineReturn = 4,
}

/// <summary>
/// The <b>only</b> mechanism that changes a posted invoice's economics (A-RULE 002). A posted
/// invoice is immutable: staff never edit a line, a discount or a total after posting — they append
/// a compensating entry that says what changed, by how much, why and who did it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Sign convention.</b> <c>DueTotal = GrandTotal - PaidTotal - Σ Amount</c>, so a <b>positive</b>
/// amount reduces what the payer owes (credit note, write-off) and a <b>negative</b> amount
/// re-opens it (a refund, which is paired with a negative <see cref="Payment"/> so the two cancel
/// and the invoice settles at zero).
/// </para>
/// <para>
/// Like <see cref="Payment"/>, an adjustment is never edited or deleted: undoing one is a further
/// adjustment with <see cref="ReversesAdjustmentId"/> set (A-RULE 003).
/// </para>
/// </remarks>
/// <param name="AdjustmentId">ULID identifying the adjustment inside its invoice.</param>
/// <param name="Kind">Why the adjustment was written.</param>
/// <param name="Amount">The signed amount; positive reduces the debt, negative re-opens it.</param>
/// <param name="Reason">Mandatory audit reason — a correction without a reason is not allowed.</param>
/// <param name="CreatedUtc">When it was written, UTC, from the injected clock.</param>
/// <param name="CreatedByUserId">The staff member who wrote it.</param>
/// <param name="OperationId">The business command that produced it (A-RULE 006 idempotency key).</param>
/// <param name="ReversesAdjustmentId">The adjustment this one offsets; <see langword="null"/> for an original.</param>
public sealed record InvoiceAdjustment(
    string AdjustmentId,
    AdjustmentKind Kind,
    Money Amount,
    string Reason,
    DateTimeOffset CreatedUtc,
    string CreatedByUserId,
    string? OperationId,
    string? ReversesAdjustmentId)
{
    /// <summary><see langword="true"/> when this entry offsets an earlier adjustment.</summary>
    public bool IsReversal => ReversesAdjustmentId is not null;
}
