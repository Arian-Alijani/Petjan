using Animora.Domain.ValueObjects;

namespace Animora.Domain.Sales;

/// <summary>What kind of thing an invoice line charges for.</summary>
public enum InvoiceLineKind
{
    /// <summary>A clinical or grooming service.</summary>
    Service = 0,

    /// <summary>A product sold or dispensed from inventory.</summary>
    Product = 1,

    /// <summary>A boarding charge (room nights or a boarding service).</summary>
    Boarding = 2,

    /// <summary>A one-off line typed by staff.</summary>
    Custom = 3,
}

/// <summary>Which upstream context handed the line to Sales.</summary>
public enum InvoiceLineSource
{
    /// <summary>Copied from a visit's billable lines.</summary>
    Visit = 0,

    /// <summary>Copied from a boarding stay's items or room charge.</summary>
    Stay = 1,

    /// <summary>Picked directly from the catalogue at the counter.</summary>
    Catalog = 2,

    /// <summary>Typed by hand; no upstream entity.</summary>
    Manual = 3,
}

/// <summary>
/// One charged line of an <see cref="Invoice"/>: what was sold, how much of it, at what unit price,
/// with its own discount and tax percentages.
/// </summary>
/// <remarks>
/// <para>
/// All money here goes through <see cref="Money.ApplyPercent(decimal)"/> — the single rounding
/// boundary of the product (CONVENTIONS.md §4). The quantity extension is expressed as
/// <c>unitPrice.ApplyPercent(q × 100)</c> rather than an inline <c>Math.Round</c>.
/// </para>
/// <para>
/// <see cref="LineTax"/> is the <i>line-local</i> tax: tax on the line net after the line's own
/// discount, ignoring any invoice-level discount. The authoritative invoice tax is computed by
/// <see cref="InvoiceTotals"/>, which pro-rates the invoice discount across the taxable lines
/// first. The line never knows about invoice-level economics.
/// </para>
/// </remarks>
/// <param name="LineId">ULID identifying the line inside its invoice.</param>
/// <param name="Kind">What kind of charge this is.</param>
/// <param name="CatalogItemId">Service/product catalogue id; <see langword="null"/> for a custom line.</param>
/// <param name="ProductId">The product sold, when the line is stock-backed.</param>
/// <param name="VariantId">The variant sold — stock movements are always per variant.</param>
/// <param name="Title">Line description as it appears on the printed invoice.</param>
/// <param name="Quantity">Amount charged, with its unit.</param>
/// <param name="UnitPrice">Price per unit at the time of sale.</param>
/// <param name="DiscountPercent">Line discount in percent, applied to <see cref="LineGross"/>.</param>
/// <param name="TaxPercent">Line tax in percent, applied to the line net.</param>
/// <param name="Source">Which upstream context produced the line.</param>
/// <param name="SourceEntityId">Id of the upstream visit line, stay item or catalogue entry.</param>
public sealed record InvoiceLine(
    string LineId,
    InvoiceLineKind Kind,
    string? CatalogItemId,
    string? ProductId,
    string? VariantId,
    string Title,
    Quantity Quantity,
    Money UnitPrice,
    decimal DiscountPercent,
    decimal TaxPercent,
    InvoiceLineSource Source,
    string? SourceEntityId)
{
    /// <summary><c>Quantity × UnitPrice</c>, before any discount or tax.</summary>
    public Money LineGross => UnitPrice.ApplyPercent(Quantity.Value * 100m);

    /// <summary>The line's own discount amount, taken off <see cref="LineGross"/>.</summary>
    public Money LineDiscount => LineGross.ApplyPercent(DiscountPercent);

    /// <summary>
    /// The line-local tax: <c>(LineGross - LineDiscount).ApplyPercent(TaxPercent)</c>. The invoice's
    /// authoritative tax is <see cref="InvoiceTotals"/>'s, which also pro-rates the invoice discount.
    /// </summary>
    public Money LineTax => LineGross.Subtract(LineDiscount).ApplyPercent(TaxPercent);

    /// <summary><c>LineGross - LineDiscount + LineTax</c>: what the line costs on its own.</summary>
    public Money LineNet => LineGross.Subtract(LineDiscount).Add(LineTax);
}
