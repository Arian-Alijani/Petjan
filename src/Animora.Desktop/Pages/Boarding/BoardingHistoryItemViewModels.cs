using Animora.Contracts.Boarding;
using Animora.Desktop.Controls;
using Animora.Desktop.Pages.History;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Animora.Desktop.Pages.Boarding;

/// <summary>
/// One kind chip of the boarding history ledger's filter row: its
/// <see cref="Kind"/>, the Persian label, the icon key and the accent/background
/// brushes plus its active state (mirrors the Sales/History kind chips).
/// </summary>
public sealed partial class BoardingHistoryKindChipViewModel : ObservableObject
{
    private static readonly IBrush s_inactiveBackground = Brushes.Transparent;
    private static readonly IBrush s_inactiveBorder = HistoryPresentation.ChipLineBrush;
    private static readonly IBrush s_mutedLabel = HistoryPresentation.ChipMutedInkBrush;

    public BoardingHistoryKindChipViewModel(BoardingHistoryKind kind)
    {
        Kind = kind;
        Label = KindLabel(kind);
        IconKey = BoardingHistoryPresentation.KindIcon(kind);
        ColorBrush = BoardingHistoryPresentation.Fill(BoardingHistoryPresentation.KindColor(kind));
        BackgroundBrush = BoardingHistoryPresentation.Fill(BoardingHistoryPresentation.KindBg(kind));
    }

    public BoardingHistoryKind Kind { get; }

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
    public static string KindLabel(BoardingHistoryKind kind) => kind switch
    {
        BoardingHistoryKind.CheckIn => "پذیرش",
        BoardingHistoryKind.ItemAdded => "افزودن مورد",
        BoardingHistoryKind.ItemRemoved => "حذف مورد",
        BoardingHistoryKind.Transferred => "انتقال",
        BoardingHistoryKind.CheckOut => "تخلیه",
        _ => "رویداد",
    };
}

/// <summary>
/// One preset of the boarding history toolbar's date range: a key, the Persian
/// label and the resolved inclusive Jalali <see cref="From"/>/<see cref="To"/>
/// bounds (empty = unbounded). Selecting one swaps the free date pickers.
/// </summary>
public sealed partial class BoardingHistoryRangeViewModel : ObservableObject
{
    public BoardingHistoryRangeViewModel(string key, string label, string from, string to)
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
/// One ledger row of the boarding history table: the kind badge, the pet +
/// note lock-up, date/time, the signed amount, the night count and the actor.
/// Every display string + brush is computed once in the constructor
/// (PERFORMANCE.md §9) — the template only binds.
/// </summary>
public sealed class BoardingHistoryRowViewModel
{
    public BoardingHistoryRowViewModel(BoardingHistoryEntry entry)
    {
        Id = entry.Id;
        StayId = entry.StayId;
        KindLabel = entry.KindLabel;
        Note = entry.Note;

        PetName = entry.PetName;
        OwnerName = string.IsNullOrEmpty(entry.OwnerName) ? "—" : entry.OwnerName;
        RoomText = "اتاق " + entry.RoomLabel;

        SpeciesIconKey = BoardingHistoryPresentation.SpeciesIconKey(entry.Species);
        SpeciesLabel = BoardingHistoryPresentation.SpeciesLabel(entry.Species);

        IconKey = BoardingHistoryPresentation.KindIcon(entry.Kind);
        BadgeBrush = BoardingHistoryPresentation.Fill(BoardingHistoryPresentation.KindBg(entry.Kind));
        BadgeIconBrush = BoardingHistoryPresentation.Fill(BoardingHistoryPresentation.KindColor(entry.Kind));
        KindTone = BoardingHistoryPresentation.KindTone(entry.Kind);

        DateText = BoardingHistoryPresentation.FaText(entry.Date);
        TimeText = string.IsNullOrEmpty(entry.Time) ? "—" : BoardingHistoryPresentation.FaText(entry.Time);

        NightsText = entry.Nights > 0
            ? BoardingHistoryPresentation.Fa(entry.Nights) + " شب"
            : "—";

        HasAmount = entry.Amount != 0 && entry.AmountSign != 0;
        AmountText = HistoryPresentation.SignedAmount(entry.Amount, entry.AmountSign);

        AmountBrush = BoardingHistoryPresentation.AmountBrush(entry.AmountSign);

        // The audit "who" (R1).
        ActorName = entry.Actor.Name;
        ActorInitials = entry.Actor.Initials;
        ActorRole = entry.Actor.RoleLabel;

        // «پتیار · یادداشت» subtitle, or just the pet when there is no note.
        Subtitle = string.IsNullOrEmpty(Note) ? PetName : PetName + " · " + Note;
    }

    public string Id { get; }

    public string StayId { get; }

    public string KindLabel { get; }

    public string Note { get; }

    public string PetName { get; }

    public string OwnerName { get; }

    public string RoomText { get; }

    public string Subtitle { get; }

    public string SpeciesIconKey { get; }

    public string SpeciesLabel { get; }

    public string IconKey { get; }

    public IBrush BadgeBrush { get; }

    public IBrush BadgeIconBrush { get; }

    public Tone KindTone { get; }

    public string DateText { get; }

    public string TimeText { get; }

    public string NightsText { get; }

    public bool HasAmount { get; }

    public string AmountText { get; }

    public IBrush AmountBrush { get; }

    /// <summary>The audit "who" — the user who performed the recorded action (R1).</summary>
    public string ActorName { get; }

    public string ActorInitials { get; }

    public string ActorRole { get; }
}
