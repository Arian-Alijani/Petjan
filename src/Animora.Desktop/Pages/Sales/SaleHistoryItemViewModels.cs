using System.Collections.Generic;
using Animora.Contracts.Sales;
using Animora.Desktop.Controls;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Animora.Desktop.Pages.Sales;

/// <summary>
/// One kind chip of the history tab's filter row: its <see cref="Kind"/>, the
/// Persian label, the icon key and the accent/background brushes plus its active
/// state (mirrors the History screen's category chips).
/// </summary>
public sealed partial class SaleHistoryKindChipViewModel : ObservableObject
{
    private static readonly IBrush s_inactiveBackground = Brushes.Transparent;
    private static readonly IBrush s_inactiveBorder =
        SaleHistoryPresentation.Fill("#e4e7ec");
    private static readonly IBrush s_mutedLabel =
        SaleHistoryPresentation.Fill("#475467");

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
/// One ledger row of the history table (mirrors the History screen's event row):
/// the kind badge, invoice/owner lock-up, date/time, signed amount and the
/// resulting invoice-state chip. Every display string + brush is computed once
/// in the constructor (PERFORMANCE.md §9) — the template only binds.
/// </summary>
public sealed class SaleHistoryRowViewModel
{
    public SaleHistoryRowViewModel(CashierHistoryEntry entry)
    {
        Id = entry.Id;
        InvoiceId = entry.InvoiceId;
        KindLabel = entry.KindLabel;
        Note = entry.Note;

        OwnerName = string.IsNullOrEmpty(entry.OwnerName) ? "مشتری گذری" : entry.OwnerName;
        PetName = entry.PetName;

        IconKey = SaleHistoryPresentation.KindIcon(entry.Kind);
        BadgeBrush = SaleHistoryPresentation.Fill(SaleHistoryPresentation.KindBg(entry.Kind));
        BadgeIconBrush = SaleHistoryPresentation.Fill(SaleHistoryPresentation.KindColor(entry.Kind));
        KindTone = SaleHistoryPresentation.KindTone(entry.Kind);

        DateText = SaleHistoryPresentation.FaText(entry.Date);
        TimeText = string.IsNullOrEmpty(entry.Time) ? "—" : SaleHistoryPresentation.FaText(entry.Time);

        var hasAmount = entry.Amount != 0 && entry.AmountSign != 0;
        HasAmount = hasAmount;
        if (!hasAmount)
        {
            AmountText = "—";
        }
        else
        {
            var sign = entry.AmountSign < 0 ? "−" : "+";
            AmountText = sign + SaleHistoryPresentation.Money(entry.Amount) + " ت";
        }

        AmountBrush = SaleHistoryPresentation.AmountBrush(entry.AmountSign);

        StateLabel = entry.StateLabel;
        StateTone = SalePresentation.StateTone(entry.State);

        // «مالک · پتیار» subtitle, or just the owner when there is no patient.
        Subtitle = string.IsNullOrEmpty(PetName) ? OwnerName : OwnerName + " · " + PetName;
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
}
