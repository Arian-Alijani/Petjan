using Animora.Domain.ValueObjects;

namespace Animora.Domain.Clinical;

/// <summary>Where a billable visit line came from.</summary>
public enum VisitLineSource
{
    /// <summary>A service from the service catalogue.</summary>
    Service = 0,

    /// <summary>A product consumed or dispensed from inventory.</summary>
    Product = 1,

    /// <summary>A one-off line typed by the doctor.</summary>
    Custom = 2,
}

/// <summary>
/// One billable line produced by the encounter. This is the <b>handoff payload</b> Sales reads to
/// build the invoice (A07/C07): the visit records <i>what was done at which unit price</i> and never
/// computes totals, discounts or tax itself — that authority belongs to the invoice aggregate alone.
/// </summary>
/// <param name="LineId">ULID identifying the line inside its visit.</param>
/// <param name="CatalogItemId">Service/product catalogue id; <see langword="null"/> for a custom line.</param>
/// <param name="Title">Line description as it should appear on the invoice.</param>
/// <param name="Quantity">Amount performed or dispensed, with its unit.</param>
/// <param name="UnitPrice">Price per unit at the time of the encounter.</param>
/// <param name="Source">Which catalogue (if any) the line came from.</param>
public sealed record VisitServiceLine(
    string LineId,
    string? CatalogItemId,
    string Title,
    Quantity Quantity,
    Money UnitPrice,
    VisitLineSource Source);
