using System;
using System.Collections.Generic;
using Animora.Contracts.Appointments;

namespace Animora.Desktop.Pages.Appointments.Export;

/// <summary>Physical paper size of the printed appointment sheet.</summary>
public enum PrintPaper
{
    A4,
    A3,
}

/// <summary>How each printed day is organised.</summary>
public enum PrintLayout
{
    /// <summary>One chronological table per day (a doctor column when several doctors are printed).</summary>
    Timeline,

    /// <summary>Per day, one sub-table per doctor (each with its own header and count).</summary>
    ByDoctor,
}

/// <summary>Row density — compact fits more rows per page for long lists.</summary>
public enum PrintDensity
{
    Comfortable,
    Compact,
}

/// <summary>
/// Everything the appointment print document needs, fully resolved by the export
/// dialog: the already-filtered appointments, the catalogs to label them and the
/// page/column choices. The builder is a pure function of this record.
/// </summary>
public sealed record AppointmentPrintOptions
{
    public required IReadOnlyList<DateOnly> Days { get; init; }

    public required IReadOnlyList<AppointmentEvent> Appointments { get; init; }

    public required IReadOnlyList<AppointmentDoctor> Doctors { get; init; }

    public required IReadOnlyDictionary<string, AppointmentType> Types { get; init; }

    /// <summary>True when every clinic doctor is included (header says «همه پزشکان»).</summary>
    public bool AllDoctors { get; init; }

    /// <summary>Clinic-local moment the document is prepared at (A24); never the device's local time.</summary>
    public Animora.Contracts.Appointments.ClinicMoment PreparedAt { get; init; }

    public string ClinicName { get; init; } = string.Empty;

    public string PreparedBy { get; init; } = string.Empty;

    public string Title { get; init; } = "فهرست نوبت‌ها";

    /// <summary>Day-only calendar mode: no clock times, rows are numbered instead.</summary>
    public bool DayOnly { get; init; }

    public PrintPaper Paper { get; init; } = PrintPaper.A4;

    public bool Landscape { get; init; }

    public PrintLayout Layout { get; init; } = PrintLayout.Timeline;

    public PrintDensity Density { get; init; } = PrintDensity.Comfortable;

    public bool NewPagePerDay { get; init; }

    public bool IncludeEmptyDays { get; init; }

    public bool IncludeSummary { get; init; } = true;

    public bool ShowCheckbox { get; init; } = true;

    public bool ShowPhone { get; init; } = true;

    public bool ShowType { get; init; } = true;

    public bool ShowStatus { get; init; } = true;

    public bool ShowNote { get; init; } = true;

    public bool ShowCode { get; init; }

    public bool ShowNotesColumn { get; init; }

    /// <summary>Opens the browser print dialog as soon as the document is ready.</summary>
    public bool AutoPrint { get; init; } = true;
}
