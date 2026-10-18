using System;
using System.Windows.Input;
using Animora.Contracts.Sales;
using Animora.Desktop.Controls;
using Animora.Desktop.Localization;
using Animora.Desktop.Pages.History;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.Sales;

/// <summary>
/// One invoice row of the «فاکتور» tab. Built for scanning: the customer block comes first (avatar, owner,
/// phone) so the reader instantly sees whose invoice it is, then the invoice number with its source pill, the
/// Jalali date, the patient, the money triplet (total / paid / due) and the state pill. Every display string and
/// brush is computed once here, so the template only binds (PERFORMANCE.md §9).
/// </summary>
public sealed class SaleInvoiceLedgerRowViewModel
{
    public SaleInvoiceLedgerRowViewModel(InvoiceSummary summary,
        Func<string, System.Threading.Tasks.Task>? open = null,
        Func<string, System.Threading.Tasks.Task>? edit = null,
        Func<string, System.Threading.Tasks.Task>? delete = null)
    {
        Id = summary.Id;
        Summary = summary;

        CustomerName = string.IsNullOrWhiteSpace(summary.OwnerName) ? "مشتری گذری" : summary.OwnerName;
        Phone = string.IsNullOrWhiteSpace(summary.Phone) ? "—" : summary.Phone;
        PetName = string.IsNullOrWhiteSpace(summary.PetName) ? "—" : summary.PetName;
        Initial = string.IsNullOrEmpty(summary.PetInitial) ? "؟" : summary.PetInitial;
        AvatarBrush = SalePresentation.AvatarBrush(summary.AvatarTone);

        Number = string.IsNullOrWhiteSpace(summary.Number)
            ? "—"
            : PersianNumberFormatter.ToPersianDigits(summary.Number);
        DateText = JalaliText(summary.IssuedAt);

        SourceLabel = summary.Kind == InvoiceKind.Visit ? "از ویزیت" : "فروش مستقیم";

        TotalText = SalePresentation.Money(summary.Total);
        var paid = summary.Total - summary.Due;
        PaidText = SalePresentation.Money(paid < 0 ? 0 : paid);
        DueText = SalePresentation.Money(summary.Due);
        DueBrush = summary.Due > 0 ? HistoryPresentation.ToneBrush(Tone.Danger) : HistoryPresentation.MutedBrush;

        HasDue = summary.Due > 0;
        StateLabel = SalePresentation.StateLabel(summary.State);
        StateBrush = SalePresentation.StateDotBrush(summary.State);
        StateTone = SalePresentation.StateTone(summary.State);
        StateBackgroundBrush = HistoryPresentation.ToneBgBrush(StateTone);

        // Only a draft is editable/deletable (the backend refuses anything else: INVOICE_IMMUTABLE), so the
        // actions are never offered on a row where they could only fail.
        IsDraft = summary.State == InvoiceState.Draft;
        CanEdit = IsDraft;
        CanDelete = IsDraft;

        OpenCommand = new AsyncRelayCommand(
            () => open is not null ? open(Id) : System.Threading.Tasks.Task.CompletedTask);
        EditCommand = new AsyncRelayCommand(
            () => edit is not null ? edit(Id) : System.Threading.Tasks.Task.CompletedTask,
            () => CanEdit);
        DeleteCommand = new AsyncRelayCommand(
            () => delete is not null ? delete(Id) : System.Threading.Tasks.Task.CompletedTask,
            () => CanDelete);
    }

    public string Id { get; }

    public InvoiceSummary Summary { get; }

    /// <summary>The owner's name, or «مشتری گذری» for a walk-in sale.</summary>
    public string CustomerName { get; }

    public string Phone { get; }

    public string PetName { get; }

    public string Initial { get; }

    public IBrush AvatarBrush { get; }

    /// <summary>The invoice number in Persian digits («—» when the draft has none yet).</summary>
    public string Number { get; }

    /// <summary>The issue date as a Persian-digit Jalali <c>yyyy/MM/dd</c> («—» when not issued yet).</summary>
    public string DateText { get; }

    /// <summary>Where the invoice came from: «از ویزیت» / «از بستری» / «فروش مستقیم».</summary>
    public string SourceLabel { get; }

    public string TotalText { get; }

    public string PaidText { get; }

    public string DueText { get; }

    /// <summary>Danger red while something is still owed, muted at zero — so a paid row reads as finished.</summary>
    public IBrush DueBrush { get; }

    public bool HasDue { get; }

    public string StateLabel { get; }

    public IBrush StateBrush { get; }

    public IBrush StateBackgroundBrush { get; }

    public Tone StateTone { get; }

    /// <summary>True while the invoice is still an open draft (editable + deletable).</summary>
    public bool IsDraft { get; }

    public bool CanEdit { get; }

    public bool CanDelete { get; }

    /// <summary>Opens the unified invoice sheet (the same screen as the cashier / owner sheet).</summary>
    public ICommand OpenCommand { get; }

    /// <summary>Opens the invoice for editing (draft only).</summary>
    public ICommand EditCommand { get; }

    /// <summary>Deletes the invoice after confirmation (draft only).</summary>
    public ICommand DeleteCommand { get; }

    /// <summary>Clinic-local issue instant → Persian-digit Jalali date, or «—».</summary>
    private static string JalaliText(DateTime? issued)
    {
        if (issued is null)
        {
            return "—";
        }

        var iso = JalaliCalendar.ToIso(JalaliDate.FromDateTime(issued.Value));
        return JalaliCalendar.Short(iso);
    }
}

/// <summary>One state-filter chip of the «فاکتور» tab («همه» / «باز» / «تسویه‌شده» / «ابطال و عودت»).</summary>
public sealed partial class SaleInvoiceLedgerFilterViewModel : ObservableObject
{
    public SaleInvoiceLedgerFilterViewModel(string key, string label)
    {
        Key = key;
        Label = label;
    }

    public string Key { get; }

    public string Label { get; }

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private string _countText = "۰";
}
