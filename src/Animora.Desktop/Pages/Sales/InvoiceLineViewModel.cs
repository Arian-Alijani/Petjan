using System;
using System.Threading.Tasks;
using Animora.Contracts.Sales;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Animora.Desktop.Pages.Sales;

/// <summary>
/// One line item of the invoice work surface, mirroring the reference's
/// <c>.sw-line</c> (<c>renderLine</c>): the tag glyph/tone, title + unit price ×
/// qty, the quantity stepper, the per-line discount box and the line subtotal
/// (with the struck original when discounted). The stepper (−/+) and the trash button are live on an
/// editable invoice: they hand the new quantity / removal back to the page through the supplied delegates
/// (the page calls the sales service, which recomputes the totals authoritatively).
/// </summary>
public sealed class InvoiceLineViewModel
{
    /// <param name="line">The contract line (carries the index + id the service needs).</param>
    /// <param name="canEdit">False for a locked (posted / void / refunded) invoice — the controls are hidden.</param>
    /// <param name="edit">Applies a new quantity to <paramref name="line"/>.</param>
    /// <param name="remove">Removes <paramref name="line"/> from the invoice.</param>
    public InvoiceLineViewModel(InvoiceLine line, bool canEdit = false,
        Func<InvoiceLine, int, Task>? edit = null, Func<InvoiceLine, Task>? remove = null)
    {
        CanEdit = canEdit && edit is not null && remove is not null;
        CanDecrease = CanEdit && line.Quantity > 1;
        IncreaseCommand = new AsyncRelayCommand(() => edit!(line, line.Quantity + 1), () => CanEdit);
        DecreaseCommand = new AsyncRelayCommand(() => edit!(line, line.Quantity - 1), () => CanDecrease);
        RemoveCommand = new AsyncRelayCommand(() => remove!(line), () => CanEdit);

        Title = line.Title;
        IconKey = line.IconKey;
        KindLabel = line.KindLabel;
        (TagBackground, TagForeground) = SalePresentation.LineTagBrushes(line.Kind, line.IsCustom);

        UnitText = SalePresentation.Money(line.UnitPrice) + " تومان × " + SalePresentation.Fa(line.Quantity);
        QuantityText = SalePresentation.Fa(line.Quantity);

        // Gross and net come from the domain read model; no money arithmetic in the UI (X26/X39).
        var sub = line.Gross;
        var net = line.Subtotal;
        var disc = sub - net;

        HasDiscount = disc > 0;
        SubtotalStruckText = SalePresentation.Money(sub);
        SubtotalText = SalePresentation.Money(net);
    }

    /// <summary>True when the invoice is editable — shows the stepper and the remove button.</summary>
    public bool CanEdit { get; }

    /// <summary>The «−» button is disabled at quantity 1 (removing is the trash button's job).</summary>
    public bool CanDecrease { get; }

    public IAsyncRelayCommand IncreaseCommand { get; }

    public IAsyncRelayCommand DecreaseCommand { get; }

    public IAsyncRelayCommand RemoveCommand { get; }

    public string Title { get; }

    public string IconKey { get; }

    public string KindLabel { get; }

    public IBrush TagBackground { get; }

    public IBrush TagForeground { get; }

    public string UnitText { get; }

    public string QuantityText { get; }

    public bool HasDiscount { get; }

    public string SubtotalStruckText { get; }

    public string SubtotalText { get; }
}

/// <summary>
/// One historical payment row of the invoice work surface, mirroring the
/// reference's <c>.sw-history-item</c> (<c>renderHistory</c>): its method chip,
/// note + timestamp and the signed amount (a refund row is danger-toned).
/// </summary>
public sealed class InvoicePaymentViewModel
{
    public InvoicePaymentViewModel(InvoicePayment payment)
    {
        (MethodBackground, MethodForeground) = SalePresentation.MethodBrushes(payment.Method);
        MethodLabel = payment.MethodLabel;

        var note = string.IsNullOrEmpty(payment.Note)
            ? (payment.Amount < 0 ? "عودت وجه" : "پرداخت")
            : payment.Note;
        NoteText = string.IsNullOrEmpty(payment.At) ? note : note + " · " + payment.At;

        IsRefund = payment.Amount < 0;
        AmountText = (IsRefund ? "− " : string.Empty) +
                     SalePresentation.Money(Math.Abs(payment.Amount)) + " تومان";
        AmountBrush = IsRefund
            ? SalePresentation.StateDotBrush(InvoiceState.Void)
            : SalePresentation.StateDotBrush(InvoiceState.Settled);
    }

    public IBrush MethodBackground { get; }

    public IBrush MethodForeground { get; }

    public string MethodLabel { get; }

    public string NoteText { get; }

    public bool IsRefund { get; }

    public string AmountText { get; }

    public IBrush AmountBrush { get; }
}

/// <summary>
/// One row of the sidebar's invoice list — either a group header («باز و در
/// انتظار» / «تسویه‌شده» / «ابطال و عودت») or an invoice card. Mirrors the
/// reference's <c>.sw-group-head</c> + <c>.sw-item</c> sequence in
/// <c>renderSidebar</c>; the View chooses the template by <see cref="IsGroupHeader"/>.
/// </summary>
public sealed partial class SaleSidebarRowViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isActive;

    private SaleSidebarRowViewModel(bool isGroupHeader)
    {
        IsGroupHeader = isGroupHeader;
    }

    public bool IsGroupHeader { get; }

    // Group header fields.
    public string GroupLabel { get; private init; } = string.Empty;

    public string GroupCount { get; private init; } = string.Empty;

    // Invoice card fields.
    public string InvoiceId { get; private init; } = string.Empty;

    /// <summary>Only a never-posted draft can be deleted outright (shows the ✕ on the card).</summary>
    public bool CanDelete { get; private init; }

    public string SourceLabel { get; private init; } = string.Empty;

    public string OwnerName { get; private init; } = string.Empty;

    public string Subtitle { get; private init; } = string.Empty;

    public string PetInitial { get; private init; } = string.Empty;

    public IBrush AvatarBrush { get; private init; } = Brushes.Transparent;

    public IBrush StateDotBrush { get; private init; } = Brushes.Transparent;

    public string TotalText { get; private init; } = string.Empty;

    public string TagText { get; private init; } = string.Empty;

    public IBrush TagForeground { get; private init; } = Brushes.Transparent;

    public static SaleSidebarRowViewModel Header(string label, int count) => new(true)
    {
        GroupLabel = label,
        GroupCount = SalePresentation.Fa(count),
    };

    public static SaleSidebarRowViewModel Card(InvoiceItemViewModel card)
    {
        var tagFg = card.TagTone switch
        {
            TagKind.Due => SalePresentation.StateDotBrush(InvoiceState.Void),
            TagKind.Ok => SalePresentation.StateDotBrush(InvoiceState.Settled),
            _ => SalePresentation.StateDotBrush(InvoiceState.Refunded),
        };

        return new SaleSidebarRowViewModel(false)
        {
            InvoiceId = card.Id,
            CanDelete = card.Summary.State == InvoiceState.Draft,
            SourceLabel = card.SourceLabel,
            OwnerName = card.OwnerName,
            Subtitle = card.Subtitle,
            PetInitial = card.PetInitial,
            AvatarBrush = card.AvatarBrush,
            StateDotBrush = card.StateDotBrush,
            TotalText = card.TotalText,
            TagText = card.TagText,
            TagForeground = tagFg,
        };
    }
}
