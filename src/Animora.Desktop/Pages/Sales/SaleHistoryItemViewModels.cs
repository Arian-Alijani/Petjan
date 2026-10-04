using System;
using System.Threading.Tasks;
using System.Windows.Input;
using Animora.Contracts.Sales;
using Animora.Desktop.Controls;
using Animora.Desktop.Pages.History;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.Sales;

/// <summary>
/// One kind chip of the history tab's filter row: its <see cref="Kind"/>, the
/// Persian label, the icon key and the accent/background brushes plus its active
/// state (mirrors the History screen's category chips).
/// </summary>
public sealed partial class SaleHistoryKindChipViewModel : ObservableObject
{
    private static readonly IBrush s_inactiveBackground = Brushes.Transparent;
    private static readonly IBrush s_inactiveBorder = HistoryPresentation.ChipLineBrush;
    private static readonly IBrush s_mutedLabel = HistoryPresentation.ChipMutedInkBrush;

    public SaleHistoryKindChipViewModel(CashierHistoryKind kind)
    {
        Kind = kind;
        Label = KindLabel(kind);
        IconKey = SaleHistoryPresentation.KindIcon(kind);
        ColorBrush = SaleHistoryPresentation.Fill(SaleHistoryPresentation.KindColor(kind));
        BackgroundBrush = SaleHistoryPresentation.Fill(SaleHistoryPresentation.KindBg(kind));
    }

    public CashierHistoryKind Kind { get; }

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
    public static string KindLabel(CashierHistoryKind kind) => kind switch
    {
        CashierHistoryKind.Accepted => "پذیرش",
        CashierHistoryKind.Paid => "پرداخت",
        CashierHistoryKind.Settled => "تسویه",
        CashierHistoryKind.Cancelled => "کنسل",
        CashierHistoryKind.Refunded => "عودت",
        CashierHistoryKind.Edited => "ویرایش",
        CashierHistoryKind.Deleted => "حذف",
        _ => "رویداد",
    };
}

/// <summary>
/// One preset of the history toolbar's date range («رجوع با تاریخ خاص»): a key,
/// the Persian label and the resolved inclusive Jalali {@link From}/{@link To}
/// bounds (empty = unbounded). Selecting one swaps the free date pickers.
/// </summary>
public sealed partial class SaleHistoryRangeViewModel : ObservableObject
{
    public SaleHistoryRangeViewModel(string key, string label, string from, string to)
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
/// One ledger row of the «تاریخچه صندوق» tab, rendered through the shared
/// <c>HistoryRowTemplate</c> (<see cref="IHistoryRowView"/>) so it is pixel-identical to
/// the main History page: kind badge, time, title («پذیرش فاکتور ۱۲۳») + note, the party
/// («مالک · پتیار»), actor, signed amount, the invoice-state chip and the kind pill.
/// Clicking the row opens the unified invoice sheet via <see cref="OpenCommand"/>
/// (routed through <c>IHistoryRecordOpener</c>). Every display string + brush is computed
/// once in the constructor (PERFORMANCE.md §9) — the template only binds.
/// </summary>
public sealed class SaleHistoryRowViewModel : IHistoryRowView
{
    /// <param name="entry">The ledger entry.</param>
    /// <param name="openInvoice">Opens the unified invoice sheet for <paramref name="entry"/>'s invoice.</param>
    public SaleHistoryRowViewModel(CashierHistoryEntry entry, Func<string, Task>? openInvoice = null)
    {
        Id = entry.Id;
        InvoiceId = entry.InvoiceId;
        KindLabel = entry.KindLabel;
        Note = entry.Note;

        OwnerName = string.IsNullOrEmpty(entry.OwnerName) ? "مشتری گذری" : entry.OwnerName;
        PetName = entry.PetName;

        var kindTone = SaleHistoryPresentation.KindTone(entry.Kind);
        IconKey = SaleHistoryPresentation.KindIcon(entry.Kind);
        BadgeBrush = HistoryPresentation.ToneBgBrush(kindTone);
        BadgeIconBrush = HistoryPresentation.ToneBrush(kindTone);
        KindTone = kindTone;

        CategoryLabel = entry.KindLabel;
        CategoryColorBrush = BadgeIconBrush;
        CategoryBgBrush = BadgeBrush;

        DateText = HistoryPresentation.FaText(entry.Date);
        TimeText = string.IsNullOrEmpty(entry.Time) ? "—" : HistoryPresentation.FaText(entry.Time);

        HasAmount = entry.Amount != 0 && entry.AmountSign != 0;
        AmountText = HistoryPresentation.SignedAmount(entry.Amount, entry.AmountSign);
        AmountBrush = HistoryPresentation.AmountBrush(HasAmount ? entry.AmountSign : 0);

        StateLabel = entry.StateLabel;
        StateTone = SalePresentation.StateTone(entry.State);
        StatusBrush = HistoryPresentation.ToneBrush(StateTone);
        StatusBgBrush = HistoryPresentation.ToneBgBrush(StateTone);

        // The audit "who" (R1).
        ActorName = entry.Actor.Name;
        ActorInitials = entry.Actor.Initials;
        ActorRole = entry.Actor.RoleLabel;

        // «مالک · پتیار» subtitle, or just the owner when there is no patient.
        Subtitle = string.IsNullOrEmpty(PetName) ? OwnerName : OwnerName + " · " + PetName;

        // Shared-row surface: title = «پذیرش فاکتور ۱۲۳»; detail = the audit note.
        Title = string.IsNullOrEmpty(InvoiceId)
            ? KindLabel + " فاکتور"
            : KindLabel + " فاکتور " + HistoryPresentation.FaText(InvoiceId);
        Detail = Note;
        HasDetail = !string.IsNullOrEmpty(Note);

        CanOpen = !string.IsNullOrEmpty(InvoiceId) && openInvoice is not null;
        OpenCommand = new AsyncRelayCommand(
            () => CanOpen ? openInvoice!(InvoiceId) : Task.CompletedTask,
            () => CanOpen);
    }

    public string Id { get; }

    public string InvoiceId { get; }

    public string KindLabel { get; }

    public string Note { get; }

    public string OwnerName { get; }

    public string PetName { get; }

    public string Subtitle { get; }

    public string IconKey { get; }

    public IBrush BadgeBrush { get; }

    public IBrush BadgeIconBrush { get; }

    public Tone KindTone { get; }

    public string DateText { get; }

    public string TimeText { get; }

    public bool HasAmount { get; }

    public string AmountText { get; }

    public IBrush AmountBrush { get; }

    public string StateLabel { get; }

    public Tone StateTone { get; }

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

    /// <summary>The party column: «مالک · پتیار».</summary>
    public string Entity => Subtitle;

    public string CategoryLabel { get; }

    public IBrush CategoryColorBrush { get; }

    public IBrush CategoryBgBrush { get; }

    /// <summary>The invoice-state chip label (the resulting state after this event).</summary>
    public string StatusLabel => StateLabel;

    public IBrush StatusBrush { get; }

    public IBrush StatusBgBrush { get; }

    /// <summary>True when the row has an invoice to open.</summary>
    public bool CanOpen { get; }

    /// <summary>Opens the unified invoice sheet (same screen as the Cashier / Owner sheet).</summary>
    public ICommand OpenCommand { get; }
}
