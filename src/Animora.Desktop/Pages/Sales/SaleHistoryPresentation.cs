using System.Collections.Generic;
using Animora.Contracts.Sales;
using Animora.Desktop.Controls;
using Animora.Desktop.Pages.History;
using Avalonia.Media;

namespace Animora.Desktop.Pages.Sales;

/// <summary>
/// Presentation adapters for the «تاریخچه صندوق» tab. Only the Sales-specific
/// kind registry (icon / tone / chip order) lives here — every colour, money,
/// digit and brush helper <b>delegates</b> to <see cref="HistoryPresentation"/>,
/// the single palette authority shared by the main History page and all three
/// in-page history tabs, so chips and badges are pixel-identical everywhere.
/// </summary>
public static class SaleHistoryPresentation
{
    // ── Formatting (delegated) ───────────────────────────────────────────

    /// <summary>Persian digits for a plain integer.</summary>
    public static string Fa(long value) => HistoryPresentation.Fa(value);

    /// <summary>Persian digits for arbitrary text, preserving non-digit characters.</summary>
    public static string FaText(string? text) => HistoryPresentation.FaText(text);

    /// <summary>A Persian-grouped integer (no unit).</summary>
    public static string Money(long value) => HistoryPresentation.Money(value);

    /// <summary>A shared solid fill brush from a hex string (PERFORMANCE.md §10).</summary>
    public static IBrush Fill(string hex) => HistoryPresentation.Fill(hex);

    // ── Kind registry (Sales-specific) ───────────────────────────────────

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

    /// <summary>The kind's <see cref="Tone"/> — the one input every colour below derives from.</summary>
    public static Tone KindTone(CashierHistoryKind kind) => kind switch
    {
        CashierHistoryKind.Accepted => Tone.Info,
        CashierHistoryKind.Paid => Tone.Warn,
        CashierHistoryKind.Settled => Tone.Ok,
        CashierHistoryKind.Cancelled => Tone.Danger,
        CashierHistoryKind.Refunded => Tone.Lilac,
        CashierHistoryKind.Edited => Tone.Lilac,
        CashierHistoryKind.Deleted => Tone.Danger,
        _ => Tone.Neutral,
    };

    /// <summary>The kind's accent text color (shared tone palette).</summary>
    public static string KindColor(CashierHistoryKind kind) => HistoryPresentation.ToneColor(KindTone(kind));

    /// <summary>The kind icon badge's background (shared tone palette).</summary>
    public static string KindBg(CashierHistoryKind kind) => HistoryPresentation.ToneBg(KindTone(kind));

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

    /// <summary>The signed-amount brush (delegated: inflow = ok, outflow = danger).</summary>
    public static IBrush AmountBrush(int amountSign) => HistoryPresentation.AmountBrush(amountSign);
}
