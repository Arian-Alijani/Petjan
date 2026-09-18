using Animora.Contracts.Purchasing;
using Animora.Desktop.Controls;

namespace Animora.Desktop.Pages.Purchasing;

/// <summary>
/// One KPI stat card of the Purchase screen's stat row, mirroring a reference
/// <c>.stat</c> block (<c>Animora-UI/purchase.html</c>): its large Persian value
/// (e.g. «۶»), its label («سفارش فعال») and the delta chip (۱۲٪↑ / ۴٪↓) with its
/// up/down direction. <see cref="Tone"/> drives the tinted icon tile and
/// <see cref="IconKey"/> the tile glyph, both resolved by the View.
/// </summary>
public sealed class PurchaseKpiViewModel
{
    public PurchaseKpiViewModel(PurchaseKpi kpi)
    {
        Value = kpi.Value;
        Label = kpi.Label;
        DeltaText = kpi.DeltaText;
        IsDeltaPositive = kpi.IsDeltaPositive;
        IconKey = kpi.IconKey;
        Tone = kpi.Tone switch
        {
            "info" => Tone.Info,
            "warn" => Tone.Warn,
            "danger" => Tone.Danger,
            "ok" => Tone.Ok,
            "lilac" => Tone.Lilac,
            "peach" => Tone.Peach,
            "brand" => Tone.Brand,
            _ => Tone.Neutral,
        };
    }

    public string Value { get; }

    public string Label { get; }

    public string DeltaText { get; }

    public bool IsDeltaPositive { get; }

    public string IconKey { get; }

    public Tone Tone { get; }
}

/// <summary>
/// One purchase-order row of the Purchase screen's table, mirroring a reference
/// <c>tbody tr</c> (<c>Animora-UI/purchase.html</c> lines 69–98): its number,
/// supplier, Jalali date, item count, total amount, settlement method and status
/// pill. The status pill's tone maps 1:1 to the reference's
/// <c>pill-ok</c>/<c>pill-warn</c> classes.
/// </summary>
public sealed class PurchaseOrderRowViewModel
{
    public PurchaseOrderRowViewModel(PurchaseOrderSummary order)
    {
        Id = order.Id;
        Number = order.Number;
        Supplier = order.Supplier;
        OrderDate = order.OrderDate;
        ItemCountText = order.ItemCountText;
        AmountText = order.AmountText;
        Settlement = order.Settlement;

        (StatusLabel, StatusTone) = order.Status switch
        {
            PurchaseOrderStatus.InProgress => ("در حین", Tone.Warn),
            PurchaseOrderStatus.Finalized => ("ثبت نهایی", Tone.Ok),
            _ => ("تحویل شد", Tone.Ok),
        };
    }

    public string Id { get; }

    public string Number { get; }

    public string Supplier { get; }

    public string OrderDate { get; }

    public string ItemCountText { get; }

    public string AmountText { get; }

    public string Settlement { get; }

    public string StatusLabel { get; }

    public Tone StatusTone { get; }
}
