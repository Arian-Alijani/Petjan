using System.Collections.Generic;
using Animora.Contracts.Vaccination;
using Animora.Desktop.Controls;
using Animora.Desktop.Pages.History;
using Avalonia.Media;

namespace Animora.Desktop.Pages.Vaccination;

/// <summary>
/// Presentation adapters for the «تاریخچه» section of the Vaccination screen.
/// Only the Vaccination-specific kind registry (icon / tone / chip order) lives
/// here — every colour, digit and brush helper <b>delegates</b> to
/// <see cref="HistoryPresentation"/>, the single palette authority shared by
/// the main History page and all three in-page history tabs.
/// </summary>
public static class VaccinationHistoryPresentation
{
    // ── Formatting (delegated) ───────────────────────────────────────────

    /// <summary>Persian digits for a plain integer.</summary>
    public static string Fa(long value) => HistoryPresentation.Fa(value);

    /// <summary>Persian digits for arbitrary text, preserving non-digit characters.</summary>
    public static string FaText(string? text) => HistoryPresentation.FaText(text);

    /// <summary>A shared solid fill brush from a hex string (PERFORMANCE.md §10).</summary>
    public static IBrush Fill(string hex) => HistoryPresentation.Fill(hex);

    // ── Kind registry (Vaccination-specific) ─────────────────────────────

    /// <summary>Icon resource key per kind (maps to Resources/Theme/Icons.axaml).</summary>
    public static string KindIcon(VaccinationHistoryKind kind) => kind switch
    {
        VaccinationHistoryKind.Administered => "IconSyringe",
        VaccinationHistoryKind.Scheduled => "IconCalendar",
        VaccinationHistoryKind.SmsSent => "IconSms",
        VaccinationHistoryKind.Rescheduled => "IconRefresh",
        VaccinationHistoryKind.Updated => "IconEdit",
        VaccinationHistoryKind.Cancelled => "IconX",
        _ => "IconTimeline",
    };

    /// <summary>The kind's <see cref="Tone"/> — the one input every colour below derives from.</summary>
    public static Tone KindTone(VaccinationHistoryKind kind) => kind switch
    {
        VaccinationHistoryKind.Administered => Tone.Ok,
        VaccinationHistoryKind.Scheduled => Tone.Info,
        VaccinationHistoryKind.SmsSent => Tone.Lilac,
        VaccinationHistoryKind.Rescheduled => Tone.Warn,
        VaccinationHistoryKind.Updated => Tone.Brand,
        VaccinationHistoryKind.Cancelled => Tone.Danger,
        _ => Tone.Neutral,
    };

    /// <summary>The kind's accent text color (shared tone palette).</summary>
    public static string KindColor(VaccinationHistoryKind kind) => HistoryPresentation.ToneColor(KindTone(kind));

    /// <summary>The kind icon badge's background (shared tone palette).</summary>
    public static string KindBg(VaccinationHistoryKind kind) => HistoryPresentation.ToneBg(KindTone(kind));

    /// <summary>All kinds in the chip row's declared order (the record lifecycle).</summary>
    public static IReadOnlyList<VaccinationHistoryKind> AllKinds { get; } =
    [
        VaccinationHistoryKind.Administered,
        VaccinationHistoryKind.Scheduled,
        VaccinationHistoryKind.SmsSent,
        VaccinationHistoryKind.Rescheduled,
        VaccinationHistoryKind.Updated,
        VaccinationHistoryKind.Cancelled,
    ];
}
