using Animora.Contracts.Vaccination;
using Animora.Desktop.Controls;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Animora.Desktop.Pages.Vaccination;

/// <summary>
/// One kind chip of the vaccination history ledger's filter row: its
/// <see cref="Kind"/>, the Persian label, the icon key and the accent/background
/// brushes plus its active state (mirrors the Sales/History kind chips).
/// </summary>
public sealed partial class VaccinationHistoryKindChipViewModel : ObservableObject
{
    private static readonly IBrush s_inactiveBackground = Brushes.Transparent;
    private static readonly IBrush s_inactiveBorder =
        VaccinationHistoryPresentation.Fill("#e4e7ec");
    private static readonly IBrush s_mutedLabel =
        VaccinationHistoryPresentation.Fill("#475467");

    public VaccinationHistoryKindChipViewModel(VaccinationHistoryKind kind)
    {
        Kind = kind;
        Label = KindLabel(kind);
        IconKey = VaccinationHistoryPresentation.KindIcon(kind);
        ColorBrush = VaccinationHistoryPresentation.Fill(VaccinationHistoryPresentation.KindColor(kind));
        BackgroundBrush = VaccinationHistoryPresentation.Fill(VaccinationHistoryPresentation.KindBg(kind));
    }

    public VaccinationHistoryKind Kind { get; }

    public string Label { get; }

    public string IconKey { get; }

    /// <summary>The kind's accent colour — always shown on the icon so chips stay identifiable.</summary>
    public IBrush ColorBrush { get; }

    /// <summary>The kind's tinted surface, used as the fill when the chip is active.</summary>
    public IBrush BackgroundBrush { get; }

    /// <summary>Chip fill: the kind's tinted surface when active, transparent otherwise.</summary>
    public IBrush ChipBackground => IsActive ? BackgroundBrush : s_inactiveBackground;

    /// <summary>Chip border: the kind's accent colour when active, a muted line otherwise.</summary>
    public IBrush ChipBorder => IsActive ? ColorBrush : s_inactiveBorder;

    /// <summary>Label colour: the kind's accent when active, a muted ink otherwise.</summary>
    public IBrush LabelBrush => IsActive ? ColorBrush : s_mutedLabel;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChipBackground))]
    [NotifyPropertyChangedFor(nameof(ChipBorder))]
    [NotifyPropertyChangedFor(nameof(LabelBrush))]
    private bool _isActive;

    /// <summary>The kind's Persian label (kept in the ViewModel layer, not Contracts).</summary>
    public static string KindLabel(VaccinationHistoryKind kind) => kind switch
    {
        VaccinationHistoryKind.Administered => "تزریق شد",
        VaccinationHistoryKind.Scheduled => "زمان‌بندی",
        VaccinationHistoryKind.SmsSent => "پیامک",
        VaccinationHistoryKind.Rescheduled => "باززمان‌بندی",
        _ => "رویداد",
    };
}

/// <summary>
/// One preset of the vaccination history toolbar's date range: a key, the
/// Persian label and the resolved inclusive Jalali <see cref="From"/>/
/// <see cref="To"/> bounds (empty = unbounded).
/// </summary>
public sealed partial class VaccinationHistoryRangeViewModel : ObservableObject
{
    public VaccinationHistoryRangeViewModel(string key, string label, string from, string to)
    {
        Key = key;
        Label = label;
        From = from;
        To = to;
    }

    /// <summary>today | 7 | 30 | all — the range preset keys.</summary>
    public string Key { get; }

    public string Label { get; }

    /// <summary>Inclusive lower bound (ASCII Jalali), or empty for unbounded.</summary>
    public string From { get; }

    /// <summary>Inclusive upper bound (ASCII Jalali), or empty for unbounded.</summary>
    public string To { get; }

    [ObservableProperty]
    private bool _isActive;
}

/// <summary>
/// One ledger row of the vaccination history table: the kind badge, the
/// vaccine + pet lock-up, the dose/note subtitle, the owner, the actor, the
/// date/time and the kind chip. Every display string + brush is computed once
/// in the constructor (PERFORMANCE.md §9) — the template only binds.
/// </summary>
public sealed class VaccinationHistoryRowViewModel
{
    public VaccinationHistoryRowViewModel(VaccinationHistoryEntry entry)
    {
        Id = entry.Id;
        RecordId = entry.RecordId;
        KindLabel = entry.KindLabel;
        Note = entry.Note;

        PetName = entry.PetName;
        OwnerName = string.IsNullOrEmpty(entry.OwnerName) ? "—" : entry.OwnerName;
        VaccineName = entry.VaccineName;
        Dose = entry.Dose;

        IconKey = VaccinationHistoryPresentation.KindIcon(entry.Kind);
        BadgeBrush = VaccinationHistoryPresentation.Fill(VaccinationHistoryPresentation.KindBg(entry.Kind));
        BadgeIconBrush = VaccinationHistoryPresentation.Fill(VaccinationHistoryPresentation.KindColor(entry.Kind));
        KindTone = VaccinationHistoryPresentation.KindTone(entry.Kind);

        DateText = VaccinationHistoryPresentation.FaText(entry.Date);
        TimeText = string.IsNullOrEmpty(entry.Time) ? "—" : VaccinationHistoryPresentation.FaText(entry.Time);

        // «واکسن · دوز» subtitle, or just the vaccine when there is no dose.
        var vaccineLine = string.IsNullOrEmpty(Dose) ? VaccineName : VaccineName + " · " + Dose;
        Subtitle = string.IsNullOrEmpty(Note) ? vaccineLine : vaccineLine + " — " + Note;

        // The audit "who" (R1).
        ActorName = entry.Actor.Name;
        ActorInitials = entry.Actor.Initials;
        ActorRole = entry.Actor.RoleLabel;
    }

    public string Id { get; }

    public string RecordId { get; }

    public string KindLabel { get; }

    public string Note { get; }

    public string PetName { get; }

    public string OwnerName { get; }

    public string VaccineName { get; }

    public string Dose { get; }

    public string Subtitle { get; }

    public string IconKey { get; }

    public IBrush BadgeBrush { get; }

    public IBrush BadgeIconBrush { get; }

    public Tone KindTone { get; }

    public string DateText { get; }

    public string TimeText { get; }

    /// <summary>The audit "who" — the user who performed the recorded action (R1).</summary>
    public string ActorName { get; }

    public string ActorInitials { get; }

    public string ActorRole { get; }
}
