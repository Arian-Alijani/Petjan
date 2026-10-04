using System;
using System.Threading.Tasks;
using System.Windows.Input;
using Animora.Contracts.History;
using Animora.Contracts.Vaccination;
using Animora.Desktop.Controls;
using Animora.Desktop.Pages.History;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.Vaccination;

/// <summary>
/// One kind chip of the vaccination history ledger's filter row: its
/// <see cref="Kind"/>, the Persian label, the icon key and the accent/background
/// brushes plus its active state (mirrors the Sales/History kind chips).
/// </summary>
public sealed partial class VaccinationHistoryKindChipViewModel : ObservableObject
{
    private static readonly IBrush s_inactiveBackground = Brushes.Transparent;
    private static readonly IBrush s_inactiveBorder = HistoryPresentation.ChipLineBrush;
    private static readonly IBrush s_mutedLabel = HistoryPresentation.ChipMutedInkBrush;

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
/// One ledger row of the vaccination «تاریخچه» tab, rendered through the shared
/// <c>HistoryRowTemplate</c> (<see cref="IHistoryRowView"/>) so it is pixel-identical to the
/// main History page: kind badge, time, title («تزریق شد · پتیار») + vaccine/dose/note detail,
/// the owner, actor, the kind chip and the «واکسن» category pill (no money on this ledger).
/// Clicking the row opens the Vaccination workspace via <see cref="OpenCommand"/> (routed
/// through <c>IHistoryRecordOpener</c> with the record id). Every display string + brush is
/// computed once in the constructor (PERFORMANCE.md §9) — the template only binds.
/// </summary>
public sealed class VaccinationHistoryRowViewModel : IHistoryRowView
{
    /// <param name="entry">The ledger entry.</param>
    /// <param name="openRecord">Opens the unified record screen for <paramref name="entry"/>'s vaccination record.</param>
    public VaccinationHistoryRowViewModel(VaccinationHistoryEntry entry, Func<string, Task>? openRecord = null)
    {
        Id = entry.Id;
        RecordId = entry.RecordId;
        KindLabel = entry.KindLabel;
        Note = entry.Note;

        PetName = entry.PetName;
        OwnerName = string.IsNullOrEmpty(entry.OwnerName) ? "—" : entry.OwnerName;
        VaccineName = entry.VaccineName;
        Dose = entry.Dose;

        var kindTone = VaccinationHistoryPresentation.KindTone(entry.Kind);
        IconKey = VaccinationHistoryPresentation.KindIcon(entry.Kind);
        BadgeBrush = HistoryPresentation.ToneBgBrush(kindTone);
        BadgeIconBrush = HistoryPresentation.ToneBrush(kindTone);
        KindTone = kindTone;

        // Status chip = the record-lifecycle kind; category pill = the main page's «واکسن» kind.
        StatusBrush = BadgeIconBrush;
        StatusBgBrush = BadgeBrush;
        CategoryLabel = HistoryPresentation.KindLabel(HistoryEventKind.Vaccine);
        CategoryColorBrush = HistoryPresentation.Fill(HistoryPresentation.KindColor(HistoryEventKind.Vaccine));
        CategoryBgBrush = HistoryPresentation.Fill(HistoryPresentation.KindBg(HistoryEventKind.Vaccine));

        DateText = HistoryPresentation.FaText(entry.Date);
        TimeText = string.IsNullOrEmpty(entry.Time) ? "—" : HistoryPresentation.FaText(entry.Time);

        // No money on this ledger — the shared amount cell shows «—» in muted ink.
        HasAmount = false;
        AmountText = HistoryPresentation.SignedAmount(0, 0);
        AmountBrush = HistoryPresentation.AmountBrush(0);

        // «واکسن · دوز» subtitle, or just the vaccine when there is no dose.
        var vaccineLine = string.IsNullOrEmpty(Dose) ? VaccineName : VaccineName + " · " + Dose;
        Subtitle = string.IsNullOrEmpty(Note) ? vaccineLine : vaccineLine + " — " + Note;

        // Shared-row surface: title «تزریق شد · پتیار»; detail = vaccine/dose/note line.
        Title = string.IsNullOrEmpty(PetName) ? KindLabel : KindLabel + " · " + PetName;
        Detail = Subtitle;
        HasDetail = !string.IsNullOrEmpty(Detail);

        // The audit "who" (R1).
        ActorName = entry.Actor.Name;
        ActorInitials = entry.Actor.Initials;
        ActorRole = entry.Actor.RoleLabel;

        CanOpen = !string.IsNullOrEmpty(RecordId) && openRecord is not null;
        OpenCommand = new AsyncRelayCommand(
            () => CanOpen ? openRecord!(RecordId) : Task.CompletedTask,
            () => CanOpen);
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

    // ── IHistoryRowView (shared row template surface) ────────────────────

    public string Title { get; }

    public string Detail { get; }

    public bool HasDetail { get; }

    /// <inheritdoc cref="TimeText"/>
    public string Time => TimeText;

    /// <summary>The party column: the owner.</summary>
    public string Entity => OwnerName;

    public string CategoryLabel { get; }

    public IBrush CategoryColorBrush { get; }

    public IBrush CategoryBgBrush { get; }

    public bool HasAmount { get; }

    public string AmountText { get; }

    public IBrush AmountBrush { get; }

    /// <summary>The status chip: the record-lifecycle kind («تزریق شد» / «زمان‌بندی» / …).</summary>
    public string StatusLabel => KindLabel;

    public IBrush StatusBrush { get; }

    public IBrush StatusBgBrush { get; }

    /// <summary>True when the row has a vaccination record to open.</summary>
    public bool CanOpen { get; }

    /// <summary>Opens the unified record screen for this vaccination record.</summary>
    public ICommand OpenCommand { get; }
}
