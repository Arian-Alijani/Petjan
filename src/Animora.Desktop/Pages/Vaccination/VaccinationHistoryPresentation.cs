using System.Collections.Generic;
using Animora.Contracts.Vaccination;
using Animora.Desktop.Controls;
using Animora.Desktop.Localization;
using Avalonia.Media;

namespace Animora.Desktop.Pages.Vaccination;

/// <summary>
/// Presentation adapters for the «تاریخچه» section of the Vaccination screen:
/// the event-kind registry (label / icon key / tone / accent colors) and the
/// shared fa/date formatters. Mirrors
/// <see cref="Animora.Desktop.Pages.Sales.SaleHistoryPresentation"/> so the
/// Vaccination ledger renders identically to the Boarding/Sales/History ones.
/// Kept here so the ViewModel and View stay free of hardcoded literals and the
/// Contracts stay free of UI resources.
/// </summary>
public static class VaccinationHistoryPresentation
{
    // ── Formatting ───────────────────────────────────────────────────────

    /// <summary>Persian digits for a plain integer.</summary>
    public static string Fa(long value) =>
        PersianNumberFormatter.ToPersianDigits(value.ToString(System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>Persian digits for arbitrary text, preserving non-digit characters.</summary>
    public static string FaText(string? text) =>
        PersianNumberFormatter.ToPersianDigits(text ?? string.Empty);

    /// <summary>A shared solid fill brush from a hex string (PERFORMANCE.md §10).</summary>
    public static IBrush Fill(string hex) => BrushCache.Solid(hex);

    // ── Kind registry ────────────────────────────────────────────────────

    /// <summary>Icon resource key per kind (maps to Resources/Theme/Icons.axaml).</summary>
    public static string KindIcon(VaccinationHistoryKind kind) => kind switch
    {
        VaccinationHistoryKind.Administered => "IconSyringe",
        VaccinationHistoryKind.Scheduled => "IconCalendar",
        VaccinationHistoryKind.SmsSent => "IconSms",
        VaccinationHistoryKind.Rescheduled => "IconRefresh",
        _ => "IconTimeline",
    };

    /// <summary>The kind's accent text color.</summary>
    public static string KindColor(VaccinationHistoryKind kind) => kind switch
    {
        VaccinationHistoryKind.Administered => "#027a48",
        VaccinationHistoryKind.Scheduled => "#1570cd",
        VaccinationHistoryKind.SmsSent => "#7b5cc4",
        VaccinationHistoryKind.Rescheduled => "#b54708",
        _ => "#667085",
    };

    /// <summary>The kind icon badge's background (the matching <c>--*-bg</c> token).</summary>
    public static string KindBg(VaccinationHistoryKind kind) => kind switch
    {
        VaccinationHistoryKind.Administered => "#ecfdf3",
        VaccinationHistoryKind.Scheduled => "#eff8ff",
        VaccinationHistoryKind.SmsSent => "#f4f3ff",
        VaccinationHistoryKind.Rescheduled => "#fffaeb",
        _ => "#f1f5f6",
    };

    /// <summary>The kind's Phase 02 <see cref="Tone"/>, for the status pill.</summary>
    public static Tone KindTone(VaccinationHistoryKind kind) => kind switch
    {
        VaccinationHistoryKind.Administered => Tone.Ok,
        VaccinationHistoryKind.Scheduled => Tone.Info,
        VaccinationHistoryKind.SmsSent => Tone.Lilac,
        VaccinationHistoryKind.Rescheduled => Tone.Warn,
        _ => Tone.Neutral,
    };

    /// <summary>All kinds in the chip row's declared order (the record lifecycle).</summary>
    public static IReadOnlyList<VaccinationHistoryKind> AllKinds { get; } =
    [
        VaccinationHistoryKind.Administered,
        VaccinationHistoryKind.Scheduled,
        VaccinationHistoryKind.SmsSent,
        VaccinationHistoryKind.Rescheduled,
    ];
}
