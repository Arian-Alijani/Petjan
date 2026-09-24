using Animora.Domain.ValueObjects;

namespace Animora.Domain.Finance;

/// <summary>Which side of the double entry a line sits on.</summary>
public enum EntrySide
{
    /// <summary>Left side: increases assets and expenses.</summary>
    Debit = 0,

    /// <summary>Right side: increases liabilities, equity and revenue.</summary>
    Credit = 1,
}

/// <summary>
/// One posting of a <see cref="JournalEntry"/>: a strictly positive amount on one side of one
/// account. Direction lives in <see cref="Side"/>, never in the sign of <see cref="Amount"/> — a
/// negative debit would be an unnameable second way to express a credit.
/// </summary>
/// <remarks>
/// A line is an immutable value: it is created with its entry and never changes, because the entry
/// that owns it is append-only forever (ARCHITECTURE.md §8.5).
/// </remarks>
/// <param name="LineId">ULID identifying the line inside its entry.</param>
/// <param name="AccountCode">The posted account; always an <see cref="AccountCodes"/> constant for a rule-produced line.</param>
/// <param name="Side">Debit or credit.</param>
/// <param name="Amount">The posted amount; strictly positive and in the entry's single currency.</param>
/// <param name="Memo">Optional per-line narration shown in the ledger view.</param>
/// <param name="CostCenter">Optional analytical dimension (branch, department) for reporting.</param>
public sealed record JournalLine(
    string LineId,
    string AccountCode,
    EntrySide Side,
    Money Amount,
    string? Memo,
    string? CostCenter)
{
    /// <summary>Materialization-only constructor for the persistence store; all values are then set from columns.</summary>
    private JournalLine()
        : this(string.Empty, string.Empty, default, default, null, null)
    {
    }

    /// <summary><see langword="true"/> when the line posts to the debit side.</summary>
    public bool IsDebit => Side == EntrySide.Debit;

    /// <summary>
    /// The line's effect on the balance check: positive for a debit, negative for a credit. A
    /// balanced entry is exactly one whose lines' signed minors sum to zero.
    /// </summary>
    public long SignedMinor => IsDebit ? Amount.Minor : -Amount.Minor;

    /// <summary>This line with its side flipped — the building block of a reversal.</summary>
    public JournalLine Mirror(string lineId) =>
        this with { LineId = lineId, Side = IsDebit ? EntrySide.Credit : EntrySide.Debit };
}
