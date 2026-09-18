using System;
using System.Collections.Generic;
using Animora.Contracts.Cashier;
using Animora.Desktop.Controls;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Animora.Desktop.Pages.Cashier;

/// <summary>
/// One overview stat card (mirrors <c>renderStats</c>): a tone, icon key, the
/// formatted value (money or a plain count), the label and the footnote shown in
/// the head chip.
/// </summary>
public sealed class CashierStatViewModel
{
    public CashierStatViewModel(CashierStat stat)
    {
        IconKey = stat.IconKey;
        Tone = CashierPresentation.StatTone(stat.Tone);
        (IconBackground, IconForeground) = CashierPresentation.StatIconBrushes(stat.Tone);
        Label = stat.Label;
        Foot = stat.Foot;
        ValueText = stat.IsMoney ? CashierPresentation.Money(stat.Value) : CashierPresentation.Fa(stat.Value);
    }

    public string IconKey { get; }

    public Tone Tone { get; }

    public IBrush IconBackground { get; }

    public IBrush IconForeground { get; }

    public string Label { get; }

    public string Foot { get; }

    public string ValueText { get; }
}

/// <summary>
/// One status-tab (mirrors <c>renderTabs</c>): its key, Persian label, live count
/// badge and active state.
/// </summary>
public sealed partial class CashierTabViewModel : ObservableObject
{
    public CashierTabViewModel(CashierTabCount count)
    {
        Tab = count.Tab;
        Key = count.Tab.ToString().ToLowerInvariant();
        Label = count.Label;
        CountText = CashierPresentation.Fa(count.Count);
    }

    public CashierTab Tab { get; }

    public string Key { get; }

    public string Label { get; }

    [ObservableProperty]
    private string _countText = "۰";

    [ObservableProperty]
    private bool _isActive;
}

/// <summary>
/// One invoice row of the left-hand list (mirrors <c>renderList</c>): the receipt
/// glyph, the pet·owner lock-up, the id·date sub-line, the state pill and the
/// total/paid/due cells with the reference's state-dependent due text/colour.
/// </summary>
public sealed partial class CashierInvoiceRowViewModel : ObservableObject
{
    public CashierInvoiceRowViewModel(CashierInvoice inv)
    {
        Id = inv.Id;
        PetName = inv.PetName.Length > 0 ? inv.PetName : "—";
        OwnerName = inv.OwnerName.Length > 0 ? inv.OwnerName : "—";
        IdDateText = inv.Date.Length > 0 ? $"{inv.Id} · {inv.Date}" : inv.Id;

        StateLabel = inv.StateLabel;
        StateTone = CashierPresentation.StateTone(inv.State);

        TotalText = CashierPresentation.Money(inv.Total);

        var terminal = inv.State is CashierInvoiceState.Settled or CashierInvoiceState.Void or CashierInvoiceState.Refunded;

        DueStatusText = inv.State switch
        {
            CashierInvoiceState.Settled => "تسویه",
            CashierInvoiceState.Void => "ابطال‌شده",
            CashierInvoiceState.Refunded => "عودت داده شد",
            _ => inv.Paid > 0 ? "پرداخت: " + CashierPresentation.Money(inv.Paid) : "پرداخت نشده",
        };

        DueStatusBrush = terminal
            ? Resolve("MutedBrush", "#667085")
            : (inv.Due > 0 ? Resolve("DangerBrush", "#d92d20") : Resolve("OkBrush", "#027a48"));

        RemainingText = inv.State == CashierInvoiceState.Settled ? "—" : CashierPresentation.Money(inv.Due);
    }

    public string Id { get; }

    public string PetName { get; }

    public string OwnerName { get; }

    public string IdDateText { get; }

    public string StateLabel { get; }

    public Tone StateTone { get; }

    public string TotalText { get; }

    public string DueStatusText { get; }

    public IBrush DueStatusBrush { get; }

    public string RemainingText { get; }

    [ObservableProperty]
    private bool _isActive;

    private static IBrush Resolve(string key, string fallback) => BrushCache.Solid(fallback);
}

/// <summary>
/// One editable line row of the detail panel (mirrors <c>renderLines</c>): the
/// kind tag, title, quantity/discount editors and the derived subtotal. Writes
/// its edits back through the supplied callback so the mock recomputes.
/// </summary>
public sealed partial class CashierLineViewModel : ObservableObject
{
    private readonly Action<int, int, int> _onEdited;
    private readonly Action<int> _onRemove;

    public CashierLineViewModel(CashierInvoiceLine line, bool editable, Action<int, int, int> onEdited, Action<int> onRemove)
    {
        _onEdited = onEdited;
        _onRemove = onRemove;

        Index = line.Index;
        Title = line.Title;
        KindLabel = CashierPresentation.LineKindLabel(line.Kind);
        (TagBackground, TagForeground) = CashierPresentation.LineTagBrushes(line.Kind);

        _quantityText = CashierPresentation.Fa(line.Quantity);
        _discountText = CashierPresentation.Fa(line.DiscountPercent);

        var sub = (long)Math.Round(line.UnitPrice * line.Quantity * (100 - line.DiscountPercent) / 100.0);
        SubtotalText = CashierPresentation.Money(sub);

        IsEditable = editable;
    }

    public int Index { get; }

    public string Title { get; }

    public string KindLabel { get; }

    public IBrush TagBackground { get; }

    public IBrush TagForeground { get; }

    public bool IsEditable { get; }

    [ObservableProperty]
    private string _quantityText;

    [ObservableProperty]
    private string _discountText;

    [ObservableProperty]
    private string _subtotalText;

    public void Remove() => _onRemove(Index);

    public void SubmitEdit() =>
        _onEdited(Index, ParseInt(QuantityText, 1), ParseInt(DiscountText, 0));

    private static int ParseInt(string? text, int fallback)
    {
        var ascii = Localization.PersianNumberFormatter.ToAsciiDigits(text).Trim();
        return int.TryParse(ascii, out var v) ? v : fallback;
    }
}

/// <summary>One payment-history row (mirrors <c>renderPaymentHistory</c>).</summary>
public sealed class CashierPaymentViewModel
{
    public CashierPaymentViewModel(CashierPayment payment)
    {
        MethodLabel = payment.MethodLabel;
        (MethodBackground, MethodForeground) = CashierPresentation.MethodBrushes(payment.Method);

        var meta = string.IsNullOrEmpty(payment.Reference) ? string.Empty : "ref: " + payment.Reference;
        if (!string.IsNullOrEmpty(payment.Note))
        {
            meta = meta.Length > 0 ? meta + " · " + payment.Note : payment.Note;
        }

        MetaText = meta;
        AtText = payment.At;
        IsNegative = payment.Amount < 0;

        var sign = payment.Amount > 0 ? "+" : string.Empty;
        AmountText = sign + CashierPresentation.Money(payment.Amount);
    }

    public string MethodLabel { get; }

    public IBrush MethodBackground { get; }

    public IBrush MethodForeground { get; }

    public string MetaText { get; }

    public string AtText { get; }

    public bool IsNegative { get; }

    public string AmountText { get; }
}

/// <summary>A payment-method chip (mirrors the reference's method row).</summary>
public sealed partial class CashierMethodViewModel : ObservableObject
{
    public CashierMethodViewModel(CashierPaymentMethod method, string label, string iconKey)
    {
        Method = method;
        Label = label;
        IconKey = iconKey;
    }

    public CashierPaymentMethod Method { get; }

    public string Label { get; }

    public string IconKey { get; }

    [ObservableProperty]
    private bool _isOn;
}

/// <summary>A pay-amount preset button (mirrors <c>.cs-pay-presets button</c>).</summary>
public sealed class CashierPresetViewModel
{
    public CashierPresetViewModel(string text, long value, bool isFull)
    {
        Text = text;
        Value = value;
        IsFull = isFull;
    }

    public string Text { get; }

    public long Value { get; }

    public bool IsFull { get; }
}

/// <summary>One option of the add-line select (mirrors the reference's optgroups).</summary>
public sealed class CashierCatalogOptionViewModel
{
    public CashierCatalogOptionViewModel(CashierCatalogItem item)
    {
        Id = item.Id;
        Label = $"{CashierPresentation.LineKindLabel(item.Kind)} · {item.Title} · {CashierPresentation.Money(item.Price)}";
    }

    public string Id { get; }

    public string Label { get; }
}

/// <summary>One option of the insurance select (mirrors <c>C().INSURERS</c>).</summary>
public sealed class CashierInsurerOptionViewModel
{
    public CashierInsurerOptionViewModel(CashierInsurer insurer, bool isNone)
    {
        Id = isNone ? string.Empty : insurer.Id;
        Label = isNone
            ? "بدون بیمه"
            : $"{insurer.Name} ({CashierPresentation.Fa(insurer.Percent)}٪)";
        IsNone = isNone;
    }

    public string Id { get; }

    public string Label { get; }

    public bool IsNone { get; }
}

/// <summary>One discount-kind option (mirrors <c>#cs-disc-kind</c>).</summary>
public sealed class CashierDiscountOptionViewModel
{
    public CashierDiscountOptionViewModel(CashierDiscountKind kind, string label)
    {
        Kind = kind;
        Label = label;
    }

    public CashierDiscountKind Kind { get; }

    public string Label { get; }
}

/// <summary>
/// The selected invoice's detail shape (mirrors <c>renderDetail</c>): the header
/// lock-up, the pre-formatted totals rows and the edibility flags that gate the
/// line editors, discount/insurance controls and footer actions.
/// </summary>
public sealed class CashierDetailViewModel
{
    public CashierDetailViewModel(CashierInvoiceDetail detail)
    {
        Detail = detail;
        Id = detail.Id;
        StateLabel = detail.StateLabel;
        StateTone = CashierPresentation.StateTone(detail.State);
        LineCountText = $"{CashierPresentation.Fa(detail.Lines.Count)} ردیف";

        PetTitle = detail.PetName.Length > 0 ? detail.PetName : "فاکتور";
        OwnerName = detail.OwnerName.Length > 0 ? detail.OwnerName : "—";
        Phone = detail.Phone.Length > 0 ? detail.Phone : "—";
        CustomerLine = $"مالک: {OwnerName} · {Phone} · {detail.Date}";

        IsEditable = detail.IsEditable;
        HasPayments = detail.Payments.Count > 0;

        PaymentCountText = $"{CashierPresentation.Fa(detail.Payments.Count)} مورد";

        BaseText = CashierPresentation.Money(detail.Base);
        DiscountText = "–" + CashierPresentation.Money(detail.DiscountAmount);
        AfterDiscountText = CashierPresentation.Money(detail.AfterDiscount);
        TaxPercentText = CashierPresentation.Fa(detail.TaxPercent);
        TaxLabel = $"مالیات خدمات ({CashierPresentation.Fa(detail.TaxPercent)}٪)";
        TaxText = "+" + CashierPresentation.Money(detail.Tax);
        InsuranceShareText = "–" + CashierPresentation.Money(detail.InsuranceShare);
        TotalText = CashierPresentation.Money(detail.Total);
        PaidText = CashierPresentation.Money(detail.Paid);
        DueText = CashierPresentation.Money(detail.Due);

        HasDue = detail.Due > 0;
        HasInsurance = !string.IsNullOrEmpty(detail.InsuranceOrgId);

        ShowVoid = detail.IsEditable;
        ShowRefund = detail.State is CashierInvoiceState.Partial or CashierInvoiceState.Settled;
        CanPay = detail.IsEditable && detail.Paid < detail.Total;
        MaxRefund = detail.Paid;
    }

    public CashierInvoiceDetail Detail { get; }

    public string Id { get; }

    public string StateLabel { get; }

    public Tone StateTone { get; }

    public string LineCountText { get; }

    public string PetTitle { get; }

    public string OwnerName { get; }

    public string Phone { get; }

    public string CustomerLine { get; }

    public bool IsEditable { get; }

    public bool HasPayments { get; }

    public string PaymentCountText { get; }

    public string BaseText { get; }

    public string DiscountText { get; }

    public string AfterDiscountText { get; }

    public string TaxPercentText { get; }

    public string TaxLabel { get; }

    public string TaxText { get; }

    public string InsuranceShareText { get; }

    public string TotalText { get; }

    public string PaidText { get; }

    public string DueText { get; }

    public bool HasDue { get; }

    public bool HasInsurance { get; }

    public bool ShowVoid { get; }

    public bool ShowRefund { get; }

    public bool CanPay { get; }

    public long MaxRefund { get; }

    public IReadOnlyList<CashierInvoiceLine> Lines => Detail.Lines;

    public IReadOnlyList<CashierPayment> Payments => Detail.Payments;
}
