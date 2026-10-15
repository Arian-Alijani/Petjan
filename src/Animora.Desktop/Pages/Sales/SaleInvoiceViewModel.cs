using System.Collections.Generic;
using Animora.Contracts.Sales;
using Avalonia.Media;

namespace Animora.Desktop.Pages.Sales;

/// <summary>
/// The selected invoice's display shape for the work surface — wraps an
/// <see cref="InvoiceDetail"/> and pre-formats the header/customer/summary
/// values the reference's <c>renderInvoiceContent</c> renders (state pill,
/// guest customer chip, visit context, totals, paid/due chips and the
/// editability flags that gate the add/pay/void/refund actions).
/// </summary>
public sealed class SaleInvoiceViewModel
{
    /// <param name="canPost">The current user's <c>sales.post</c> capability (D08). Without it the pay CTA must not
    /// be offered at all: the backend gate would refuse every attempt, so showing it is a dead affordance that
    /// produces a repeated <c>PERMISSION_DENIED</c> instead of an explanation.</param>
    public SaleInvoiceViewModel(InvoiceDetail detail, bool canPost)
    {
        Detail = detail;
        Id = detail.Id;
        State = detail.State;
        StateLabel = detail.StateLabel;
        StateBrush = SalePresentation.StateDotBrush(detail.State);

        IsLocked = detail.IsLocked;
        CanEdit = !detail.IsLocked;
        IsTerminalVoid = detail.State == InvoiceState.Void;
        IsRefunded = detail.State == InvoiceState.Refunded;

        // A posted invoice is locked too, so «locked» alone must never read as «voided»: each state gets its own
        // banner (red = void, amber = refunded, green = successfully posted / settled).
        IsVoidBanner = IsTerminalVoid;
        IsRefundBanner = IsRefunded;
        IsSuccessBanner = detail.State is InvoiceState.Settled or InvoiceState.Partial;
        (BannerText, BannerSubText) = detail.State switch
        {
            InvoiceState.Void => ("فاکتور ابطال شد", "این فاکتور دیگر قابل ویرایش یا پرداخت نیست."),
            InvoiceState.Refunded => ("فاکتور مسترد شد", "مبلغ این فاکتور به مشتری بازگردانده شده است."),
            InvoiceState.Settled => ("فاکتور با موفقیت ثبت و تسویه شد", "پرداخت کامل ثبت شد؛ فاکتور قفل است و قابل ویرایش نیست."),
            InvoiceState.Partial => ("فاکتور با موفقیت ثبت شد", "بخشی از مبلغ پرداخت شده و مانده هنوز تسویه نشده است."),
            _ => (string.Empty, string.Empty),
        };

        AvatarBrush = SalePresentation.AvatarBrush(detail.AvatarTone);
        PetInitial = detail.PetInitial;

        IsGuest = detail.IsGuest;
        CustomerName = detail.IsGuest
            ? "مشتری گذری — انتخاب مشتری"
            : detail.PetName.Length > 0 ? detail.OwnerName + " · " + detail.PetName : detail.OwnerName;
        Phone = detail.IsGuest ? "بدون پرونده — برای انتخاب کلیک کنید" : detail.Phone;
        IsVisitCustomer = detail.Kind == InvoiceKind.Visit;

        HasVisitContext = detail.HasVisitContext;
        VisitContextText = detail.HasVisitContext
            ? $"فاکتور از ویزیت پایان‌یافته توسط {detail.VisitDoctorName}"
              + (string.IsNullOrEmpty(detail.VisitCompletedAt) ? "" : $" — {detail.VisitCompletedAt}")
            : string.Empty;

        InsuranceLabel = detail.InsuranceLabel;
        HasInsurance = detail.HasInsurance;
        HasDiscount = detail.HasDiscount;
        HasDue = detail.Due > 0;
        ShowPaidInfo = detail.Paid > 0 || detail.State == InvoiceState.Partial;
        ShowPayCta = !detail.IsLocked && detail.Lines.Count > 0 && canPost;
        ShowRefund = detail.State is InvoiceState.Partial or InvoiceState.Settled;

        BaseText = SalePresentation.Money(detail.Base);
        DiscountText = "− " + SalePresentation.Money(detail.DiscountAmount) + " تومان";
        TaxPercentText = SalePresentation.Fa(detail.TaxPercent);
        TaxText = SalePresentation.Money(detail.Tax);
        InsuranceShareText = "− " + SalePresentation.Money(detail.InsuranceShare) + " تومان";
        TotalText = SalePresentation.Money(detail.Total);
        PaidText = SalePresentation.Money(detail.Paid);
        DueText = SalePresentation.Money(detail.Due);
    }

    public InvoiceDetail Detail { get; }

    public string Id { get; }

    public InvoiceState State { get; }

    public string StateLabel { get; }

    public IBrush StateBrush { get; }


    public bool IsLocked { get; }

    public bool CanEdit { get; }

    public bool IsTerminalVoid { get; }

    public bool IsRefunded { get; }

    public bool IsVoidBanner { get; }

    public bool IsRefundBanner { get; }

    public bool IsSuccessBanner { get; }

    public string BannerText { get; }

    public string BannerSubText { get; }

    public IBrush AvatarBrush { get; }

    public string PetInitial { get; }

    public bool IsGuest { get; }

    public string CustomerName { get; }

    public string Phone { get; }

    public bool IsVisitCustomer { get; }

    public bool HasVisitContext { get; }

    public string VisitContextText { get; }

    public string InsuranceLabel { get; }

    public bool HasInsurance { get; }

    public bool HasDiscount { get; }

    public bool HasDue { get; }

    public bool ShowPaidInfo { get; }

    public bool ShowPayCta { get; }

    public bool ShowRefund { get; }

    public string BaseText { get; }

    public string DiscountText { get; }

    public string TaxPercentText { get; }

    public string TaxText { get; }

    public string InsuranceShareText { get; }

    public string TotalText { get; }

    public string PaidText { get; }

    public string DueText { get; }
}
