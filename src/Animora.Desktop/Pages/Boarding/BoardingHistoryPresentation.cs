using System.Collections.Generic;
using Animora.Contracts.Boarding;
using Animora.Desktop.Controls;
using Animora.Desktop.Pages.History;
using Avalonia.Media;

namespace Animora.Desktop.Pages.Boarding;

/// <summary>
/// Presentation adapters for the «تاریخچه» ledger of the Boarding screen. Only
/// the Boarding-specific kind registry (icon / tone / chip order) and the
/// species helpers live here — every colour, money, digit and brush helper
/// <b>delegates</b> to <see cref="HistoryPresentation"/>, the single palette
/// authority shared by the main History page and all three in-page tabs.
/// </summary>
public static class BoardingHistoryPresentation
{
    // ── Formatting (delegated) ───────────────────────────────────────────

    /// <summary>Persian digits for a plain integer.</summary>
    public static string Fa(long value) => HistoryPresentation.Fa(value);

    /// <summary>Persian digits for arbitrary text, preserving non-digit characters.</summary>
    public static string FaText(string? text) => HistoryPresentation.FaText(text);

    /// <summary>A Persian-grouped integer (no unit) — identical to the main History page.</summary>
    public static string Money(long value) => HistoryPresentation.Money(value);

    /// <summary>A shared solid fill brush from a hex string (PERFORMANCE.md §10).</summary>
    public static IBrush Fill(string hex) => HistoryPresentation.Fill(hex);

    // ── Kind registry (Boarding-specific) ────────────────────────────────

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

    /// <summary>The kind's <see cref="Tone"/> — the one input every colour below derives from.</summary>
    public static Tone KindTone(BoardingHistoryKind kind) => kind switch
    {
        BoardingHistoryKind.CheckIn => Tone.Info,
        BoardingHistoryKind.ItemAdded => Tone.Ok,
        BoardingHistoryKind.ItemRemoved => Tone.Warn,
        BoardingHistoryKind.Transferred => Tone.Lilac,
        BoardingHistoryKind.CheckOut => Tone.Ok,
        _ => Tone.Neutral,
    };

    /// <summary>The kind's accent text color (shared tone palette).</summary>
    public static string KindColor(BoardingHistoryKind kind) => HistoryPresentation.ToneColor(KindTone(kind));

    /// <summary>The kind icon badge's background (shared tone palette).</summary>
    public static string KindBg(BoardingHistoryKind kind) => HistoryPresentation.ToneBg(KindTone(kind));

    /// <summary>All kinds in the chip row's declared order (the stay lifecycle).</summary>
    public static IReadOnlyList<BoardingHistoryKind> AllKinds { get; } =
    [
        BoardingHistoryKind.CheckIn,
        BoardingHistoryKind.ItemAdded,
        BoardingHistoryKind.ItemRemoved,
        BoardingHistoryKind.Transferred,
        BoardingHistoryKind.CheckOut,
    ];

    /// <summary>The signed-amount brush (delegated: inflow = ok, outflow = danger).</summary>
    public static IBrush AmountBrush(int amountSign) => HistoryPresentation.AmountBrush(amountSign);

    // ── Species (reused from the Boarding screen's presentation) ─────────

    /// <summary>The Persian species label (reused from <see cref="BoardingPresentation"/>).</summary>
    public static string SpeciesLabel(BoardingSpecies species) => BoardingPresentation.SpeciesLabel(species);

    /// <summary>The species icon resource key (reused from <see cref="BoardingPresentation"/>).</summary>
    public static string SpeciesIconKey(BoardingSpecies species) => BoardingPresentation.SpeciesIconKey(species);
}
