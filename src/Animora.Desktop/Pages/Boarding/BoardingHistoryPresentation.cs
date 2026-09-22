using System.Collections.Generic;
using Animora.Contracts.Boarding;
using Animora.Desktop.Controls;
using Animora.Desktop.Localization;
using Avalonia.Media;

namespace Animora.Desktop.Pages.Boarding;

/// <summary>
/// Presentation adapters for the «تاریخچه» ledger of the Boarding screen: the
/// event-kind registry (label / icon key / tone / accent colors) and the shared
/// money/fa/date formatters. Mirrors
/// <see cref="Animora.Desktop.Pages.Sales.SaleHistoryPresentation"/> so the
/// Boarding ledger renders identically to the Sales/History ones. Kept here so
/// the ViewModel and View stay free of hardcoded literals and the Contracts stay
/// free of UI resources.
/// </summary>
public static class BoardingHistoryPresentation
{
    // ── Formatting ───────────────────────────────────────────────────────

    /// <summary>Persian digits for a plain integer.</summary>
    public static string Fa(long value) => BoardingPresentation.Fa((int)value);

    /// <summary>Persian digits for arbitrary text, preserving non-digit characters.</summary>
    public static string FaText(string? text) =>
        PersianNumberFormatter.ToPersianDigits(text ?? string.Empty);

    /// <summary>The reference's <c>money(n)</c> — a Persian-grouped integer + «ریال».</summary>
    public static string Money(long value) => BoardingPresentation.Money(value);

    /// <summary>A shared solid fill brush from a hex string (PERFORMANCE.md §10).</summary>
    public static IBrush Fill(string hex) => BrushCache.Solid(hex);

    // ── Kind registry ────────────────────────────────────────────────────

    /// <summary>Icon resource key per kind (maps to Resources/Theme/Icons.axaml).</summary>
    public static string KindIcon(BoardingHistoryKind kind) => kind switch
    {
        BoardingHistoryKind.CheckIn => "IconHome",
        BoardingHistoryKind.ItemAdded => "IconPlus",
        BoardingHistoryKind.ItemRemoved => "IconTrash",
        BoardingHistoryKind.Transferred => "IconWallet",
        BoardingHistoryKind.CheckOut => "IconCheck",
        _ => "IconTimeline",
    };

    /// <summary>The kind's accent text color.</summary>
    public static string KindColor(BoardingHistoryKind kind) => kind switch
    {
        BoardingHistoryKind.CheckIn => "#1570cd",
        BoardingHistoryKind.ItemAdded => "#027a48",
        BoardingHistoryKind.ItemRemoved => "#b54708",
        BoardingHistoryKind.Transferred => "#7b5cc4",
        BoardingHistoryKind.CheckOut => "#027a48",
        _ => "#667085",
    };

    /// <summary>The kind icon badge's background (the matching <c>--*-bg</c> token).</summary>
    public static string KindBg(BoardingHistoryKind kind) => kind switch
    {
        BoardingHistoryKind.CheckIn => "#eff8ff",
        BoardingHistoryKind.ItemAdded => "#ecfdf3",
        BoardingHistoryKind.ItemRemoved => "#fffaeb",
        BoardingHistoryKind.Transferred => "#f4f3ff",
        BoardingHistoryKind.CheckOut => "#ecfdf3",
        _ => "#f1f5f6",
    };

    /// <summary>The kind's Phase 02 <see cref="Tone"/>, for the status pill.</summary>
    public static Tone KindTone(BoardingHistoryKind kind) => kind switch
    {
        BoardingHistoryKind.CheckIn => Tone.Info,
        BoardingHistoryKind.ItemAdded => Tone.Ok,
        BoardingHistoryKind.ItemRemoved => Tone.Warn,
        BoardingHistoryKind.Transferred => Tone.Lilac,
        BoardingHistoryKind.CheckOut => Tone.Ok,
        _ => Tone.Neutral,
    };

    /// <summary>All kinds in the chip row's declared order (the stay lifecycle).</summary>
    public static IReadOnlyList<BoardingHistoryKind> AllKinds { get; } =
    [
        BoardingHistoryKind.CheckIn,
        BoardingHistoryKind.ItemAdded,
        BoardingHistoryKind.ItemRemoved,
        BoardingHistoryKind.Transferred,
        BoardingHistoryKind.CheckOut,
    ];

    /// <summary>The signed-amount brush (inflow = ok, outflow = danger).</summary>
    public static IBrush AmountBrush(int amountSign) =>
        Fill(amountSign < 0 ? "#d92d20" : amountSign > 0 ? "#027a48" : "#667085");

    // ── Species (reused from the Boarding screen's presentation) ─────────

    /// <summary>The Persian species label (reused from <see cref="BoardingPresentation"/>).</summary>
    public static string SpeciesLabel(BoardingSpecies species) => BoardingPresentation.SpeciesLabel(species);

    /// <summary>The species icon resource key (reused from <see cref="BoardingPresentation"/>).</summary>
    public static string SpeciesIconKey(BoardingSpecies species) => BoardingPresentation.SpeciesIconKey(species);
}
