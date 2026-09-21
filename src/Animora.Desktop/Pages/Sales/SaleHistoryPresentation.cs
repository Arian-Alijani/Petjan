using System.Collections.Generic;
using Animora.Contracts.Sales;
using Animora.Desktop.Controls;
using Animora.Desktop.Localization;
using Avalonia.Media;

namespace Animora.Desktop.Pages.Sales;

/// <summary>
/// Presentation adapters for the «تاریخچه صندوق» tab: the kind registry
/// (label / icon key / tone / accent colors) and the shared money/fa/date
/// formatters. Kept here so the ViewModel and View stay free of hardcoded
/// literals and the Contracts stay free of UI resources (the same split used by
/// <see cref="SalePresentation"/> and the History screen's presentation).
/// </summary>
public static class SaleHistoryPresentation
{
    // ── Formatting ───────────────────────────────────────────────────────

    /// <summary>Persian digits for a plain integer (the reference's <c>toFa</c>).</summary>
    public static string Fa(long value) => SalePresentation.Fa(value);

    /// <summary>Persian digits for arbitrary text, preserving non-digit characters.</summary>
    public static string FaText(string? text) =>
        PersianNumberFormatter.ToPersianDigits(text ?? string.Empty);

    /// <summary>The reference's <c>money(n)</c> — a Persian-grouped integer.</summary>
    public static string Money(long value) => SalePresentation.Money(value);

    /// <summary>A shared solid fill brush from a hex string (PERFORMANCE.md §10).</summary>
    public static IBrush Fill(string hex) => BrushCache.Solid(hex);

    // ── Kind registry ────────────────────────────────────────────────────

    /// <summary>Icon resource key per kind (maps to Resources/Theme/Icons.axaml).</summary>
    public static string KindIcon(CashierHistoryKind kind) => kind switch
    {
        CashierHistoryKind.Accepted => "IconReceipt",
        CashierHistoryKind.Paid => "IconWallet",
        CashierHistoryKind.Settled => "IconCheck",
        CashierHistoryKind.Cancelled => "IconX",
        CashierHistoryKind.Refunded => "IconArrowDownLeft",
        CashierHistoryKind.Edited => "IconEdit",
        CashierHistoryKind.Deleted => "IconTrash",
        _ => "IconReceipt",
    };

    /// <summary>The kind's accent text color.</summary>
    public static string KindColor(CashierHistoryKind kind) => kind switch
    {
        CashierHistoryKind.Accepted => "#1570cd",
        CashierHistoryKind.Paid => "#b54708",
        CashierHistoryKind.Settled => "#027a48",
        CashierHistoryKind.Cancelled => "#d92d20",
        CashierHistoryKind.Refunded => "#7b5cc4",
        CashierHistoryKind.Edited => "#6f57c9",
        CashierHistoryKind.Deleted => "#d92d20",
        _ => "#667085",
    };

    /// <summary>The kind icon badge's background (the matching <c>--*-bg</c> token).</summary>
    public static string KindBg(CashierHistoryKind kind) => kind switch
    {
        CashierHistoryKind.Accepted => "#eff8ff",
        CashierHistoryKind.Paid => "#fffaeb",
        CashierHistoryKind.Settled => "#ecfdf3",
        CashierHistoryKind.Cancelled => "#fef3f2",
        CashierHistoryKind.Refunded => "#f4f3ff",
        CashierHistoryKind.Edited => "#efeafb",
        CashierHistoryKind.Deleted => "#fef3f2",
        _ => "#f1f5f6",
    };

    /// <summary>The kind's Phase 02 <see cref="Tone"/>, for the status pill.</summary>
    public static Tone KindTone(CashierHistoryKind kind) => kind switch
    {
        CashierHistoryKind.Accepted => Tone.Info,
        CashierHistoryKind.Paid => Tone.Warn,
        CashierHistoryKind.Settled => Tone.Ok,
        CashierHistoryKind.Cancelled => Tone.Danger,
        CashierHistoryKind.Edited => Tone.Lilac,
        CashierHistoryKind.Deleted => Tone.Danger,
        _ => Tone.Neutral,
    };

    /// <summary>
    /// All kinds in the chip row's declared order — accepts, payments/settlement,
    /// the audit edits and the terminal cancel/refund/delete.
    /// </summary>
    public static IReadOnlyList<CashierHistoryKind> AllKinds { get; } =
    [
        CashierHistoryKind.Accepted,
        CashierHistoryKind.Paid,
        CashierHistoryKind.Settled,
        CashierHistoryKind.Edited,
        CashierHistoryKind.Cancelled,
        CashierHistoryKind.Refunded,
        CashierHistoryKind.Deleted,
    ];

    /// <summary>The signed-amount brush (inflow = ok, outflow = danger).</summary>
    public static IBrush AmountBrush(int amountSign) =>
        Fill(amountSign < 0 ? "#d92d20" : amountSign > 0 ? "#027a48" : "#667085");
}
