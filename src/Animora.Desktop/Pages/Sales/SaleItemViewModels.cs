using Animora.Contracts.Sales;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Animora.Desktop.Pages.Sales;

/// <summary>
/// One tab of the sidebar's segmented switch (صف ویزیت / فاکتورها), mirroring
/// the reference's <c>#sw-seg</c> buttons (<c>renderSeg</c>): a Persian label,
/// its live count and the ready-visit pulse state.
/// </summary>
public sealed partial class SaleSegmentViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private string _countText = string.Empty;

    [ObservableProperty]
    private bool _isPulsing;

    public SaleSegmentViewModel(string key, string label)
    {
        Key = key;
        Label = label;
    }

    /// <summary><c>ready</c> | <c>invoices</c> — the reference's <c>data-seg</c> keys.</summary>
    public string Key { get; }

    public string Label { get; }
}

/// <summary>
/// One «آمادهٔ فاکتور» card of the sidebar's ready-visit list, mirroring the
/// reference's <c>.sw-item.ready</c> (<c>renderItemEnc</c>): the encounter id,
/// owner/pet, doctor, avatar tone and the previewed item-count + total.
/// </summary>
public sealed partial class ReadyEncounterItemViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isActive;

    public ReadyEncounterItemViewModel(ReadyEncounterRef reference)
    {
        Reference = reference;
        Id = reference.Id;
        OwnerName = reference.OwnerName;
        PetName = reference.PetName;
        PetKind = reference.PetKind;
        DoctorShort = reference.DoctorShort;
        AvatarBrush = SalePresentation.AvatarBrush(reference.AvatarTone);
        PetInitial = reference.PetInitial;
        ItemCountText = SalePresentation.Fa(reference.LineCount);
        PreviewTotalText = SalePresentation.Money(reference.PreviewTotal);

        var sub = PetName;
        if (!string.IsNullOrEmpty(PetKind))
        {
            sub += " · " + PetKind;
        }

        if (!string.IsNullOrEmpty(DoctorShort))
        {
            sub += " · " + DoctorShort;
        }

        Subtitle = sub;
    }

    public ReadyEncounterRef Reference { get; }

    public string Id { get; }

    public string OwnerName { get; }

    public string PetName { get; }

    public string PetKind { get; }

    public string DoctorShort { get; }

    public string Subtitle { get; }

    public string PetInitial { get; }

    public IBrush AvatarBrush { get; }

    public string ItemCountText { get; }

    public string PreviewTotalText { get; }

    /// <summary>«N آیتم · M تومان» — the ready card's CTA line (renderItemEnc).</summary>
    public string CtaText => $"{ItemCountText} آیتم · {PreviewTotalText} تومان";
}

/// <summary>
/// One invoice card of the sidebar's invoice list, mirroring the reference's
/// <c>.sw-item</c> (<c>renderItemInv</c>): its id + source pill, owner/pet +
/// phone, total and the state tag (تسویه / مانده N / ابطال / مسترد).
/// </summary>
public sealed partial class InvoiceItemViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isActive;

    public InvoiceItemViewModel(InvoiceSummary summary)
    {
        Summary = summary;
        Id = summary.Id;
        OwnerName = string.IsNullOrEmpty(summary.OwnerName) ? "مهمان" : summary.OwnerName;
        PetName = string.IsNullOrEmpty(summary.PetName) ? "بدون حیوان" : summary.PetName;
        Phone = summary.Phone;
        AvatarBrush = SalePresentation.AvatarBrush(summary.AvatarTone);
        PetInitial = summary.PetInitial;
        StateDotBrush = SalePresentation.StateDotBrush(summary.State);
        TotalText = SalePresentation.Money(summary.Total);
        DueText = SalePresentation.Money(summary.Due);

        var sub = PetName;
        if (!string.IsNullOrEmpty(Phone))
        {
            sub += " · " + Phone;
        }

        Subtitle = sub;

        IsVisit = summary.Kind == InvoiceKind.Visit;
        SourceLabel = IsVisit ? "از ویزیت" : "فروش";
        SourceIconKey = IsVisit ? "IconClipboard" : "IconCart";

        // .sw-item-tag — void/refund → muted «ابطال/مسترد», settled → ok
        // «تسویه», else danger «مانده N».
        switch (summary.State)
        {
            case InvoiceState.Void:
                TagText = "ابطال";
                TagTone = TagKind.Void;
                break;
            case InvoiceState.Refunded:
                TagText = "مسترد";
                TagTone = TagKind.Void;
                break;
            case InvoiceState.Settled:
                TagText = "تسویه";
                TagTone = TagKind.Ok;
                break;
            default:
                TagText = "مانده " + SalePresentation.Money(summary.Due);
                TagTone = TagKind.Due;
                break;
        }
    }

    public InvoiceSummary Summary { get; }

    public string Id { get; }

    public string OwnerName { get; }

    public string PetName { get; }

    public string Phone { get; }

    public string Subtitle { get; }

    public string PetInitial { get; }

    public IBrush AvatarBrush { get; }

    public IBrush StateDotBrush { get; }

    public string TotalText { get; }

    public string DueText { get; }

    public bool IsVisit { get; }

    public string SourceLabel { get; }

    public string SourceIconKey { get; }

    public string TagText { get; }

    public TagKind TagTone { get; }
}

/// <summary>The reference's three sidebar state-tag tones.</summary>
public enum TagKind
{
    /// <summary>«مانده N» — danger-red.</summary>
    Due,

    /// <summary>«تسویه» — ok-green.</summary>
    Ok,

    /// <summary>«ابطال» / «مسترد» — muted.</summary>
    Void,
}

/// <summary>
/// One group header of the sidebar's invoice list, mirroring the reference's
/// <c>.sw-group-head</c> («باز و در انتظار» / «تسویه‌شده» / «ابطال و عودت»)
/// with its live count.
/// </summary>
public sealed class InvoiceGroupViewModel
{
    public InvoiceGroupViewModel(string label, int count)
    {
        Label = label;
        CountText = SalePresentation.Fa(count);
    }

    public string Label { get; }

    public string CountText { get; }
}
