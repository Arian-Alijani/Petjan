using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Input;
using Animora.Contracts.Boarding;
using Animora.Desktop.Controls;
using Animora.Desktop.Pages.History;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

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
/// One ledger row of the boarding «تاریخچه» tab, rendered through the shared
/// <c>HistoryRowTemplate</c> (<see cref="IHistoryRowView"/>) so it is pixel-identical to the
/// main History page: kind badge, time, title («پذیرش · پتیار») + room/nights/note detail, the
/// owner, actor, signed amount, the kind chip and the species pill. Clicking the row opens the
/// Boarding workspace via <see cref="OpenCommand"/> (routed through <c>IHistoryRecordOpener</c>
/// with the stay id). Every display string + brush is computed once in the constructor
/// (PERFORMANCE.md §9) — the template only binds.
/// </summary>
public sealed class BoardingHistoryRowViewModel : IHistoryRowView
{
    /// <param name="entry">The ledger entry.</param>
    /// <param name="openStay">Opens the unified record screen for <paramref name="entry"/>'s stay.</param>
    public BoardingHistoryRowViewModel(BoardingHistoryEntry entry, Func<string, Task>? openStay = null)
    {
        Id = entry.Id;
        StayId = entry.StayId;
        KindLabel = entry.KindLabel;
        Note = entry.Note;

        PetName = entry.PetName;
        OwnerName = string.IsNullOrEmpty(entry.OwnerName) ? "—" : entry.OwnerName;
        RoomText = string.IsNullOrEmpty(entry.RoomLabel) ? string.Empty : "اتاق " + HistoryPresentation.FaText(entry.RoomLabel);

        SpeciesIconKey = BoardingHistoryPresentation.SpeciesIconKey(entry.Species);
        SpeciesLabel = BoardingHistoryPresentation.SpeciesLabel(entry.Species);

        var kindTone = BoardingHistoryPresentation.KindTone(entry.Kind);
        IconKey = BoardingHistoryPresentation.KindIcon(entry.Kind);
        BadgeBrush = HistoryPresentation.ToneBgBrush(kindTone);
        BadgeIconBrush = HistoryPresentation.ToneBrush(kindTone);
        KindTone = kindTone;

        // Status chip = the stay-lifecycle kind; category pill = the pet's species.
        StatusBrush = BadgeIconBrush;
        StatusBgBrush = BadgeBrush;
        CategoryLabel = SpeciesLabel;
        CategoryColorBrush = HistoryPresentation.ToneBrush(Tone.Lilac);
        CategoryBgBrush = HistoryPresentation.ToneBgBrush(Tone.Lilac);

        DateText = HistoryPresentation.FaText(entry.Date);
        TimeText = string.IsNullOrEmpty(entry.Time) ? "—" : HistoryPresentation.FaText(entry.Time);

        NightsText = entry.Nights > 0 ? HistoryPresentation.Fa(entry.Nights) + " شب" : "—";

        HasAmount = entry.Amount != 0 && entry.AmountSign != 0;
        AmountText = HistoryPresentation.SignedAmount(entry.Amount, entry.AmountSign);
        AmountBrush = HistoryPresentation.AmountBrush(HasAmount ? entry.AmountSign : 0);

        // The audit "who" (R1).
        ActorName = entry.Actor.Name;
        ActorInitials = entry.Actor.Initials;
        ActorRole = entry.Actor.RoleLabel;

        // «پتیار · یادداشت» subtitle, or just the pet when there is no note.
        Subtitle = string.IsNullOrEmpty(Note) ? PetName : PetName + " · " + Note;

        // Shared-row surface: title «پذیرش · پتیار»; detail «اتاق ۳ · ۲ شب · یادداشت».
        Title = string.IsNullOrEmpty(PetName) ? KindLabel : KindLabel + " · " + PetName;
        var parts = new List<string>(3);
        if (!string.IsNullOrEmpty(RoomText)) parts.Add(RoomText);
        if (entry.Nights > 0) parts.Add(NightsText);
        if (!string.IsNullOrEmpty(Note)) parts.Add(Note);
        Detail = string.Join(" · ", parts);
        HasDetail = Detail.Length > 0;

        CanOpen = !string.IsNullOrEmpty(StayId) && openStay is not null;
        OpenCommand = new AsyncRelayCommand(
            () => CanOpen ? openStay!(StayId) : Task.CompletedTask,
            () => CanOpen);
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

    /// <summary>The status chip: the stay-lifecycle kind («پذیرش» / «تخلیه» / …).</summary>
    public string StatusLabel => KindLabel;

    public IBrush StatusBrush { get; }

    public IBrush StatusBgBrush { get; }

    /// <summary>True when the row has a stay to open.</summary>
    public bool CanOpen { get; }

    /// <summary>Opens the unified record screen for this stay.</summary>
    public ICommand OpenCommand { get; }
}
