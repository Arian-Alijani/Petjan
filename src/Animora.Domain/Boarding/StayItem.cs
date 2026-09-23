using Animora.Domain.ValueObjects;

namespace Animora.Domain.Boarding;

/// <summary>What kind of thing a stay item is.</summary>
public enum StayItemKind
{
    /// <summary>A boarding service from the boarding-service catalogue.</summary>
    BoardingService = 0,

    /// <summary>A clinical service performed during the stay.</summary>
    ClinicService = 1,

    /// <summary>A product consumed or dispensed from inventory.</summary>
    Product = 2,

    /// <summary>A one-off line typed by staff.</summary>
    Custom = 3,
}

/// <summary>
/// One consumed item on a boarding stay: a service, product or custom charge recorded while the pet
/// was boarded. This is part of the <b>handoff payload</b> Sales reads to build the invoice (A07);
/// the stay records <i>what was consumed at which unit price</i> and never computes totals itself —
/// that authority belongs to the invoice aggregate alone.
/// </summary>
/// <param name="ItemId">ULID identifying the item inside its stay.</param>
/// <param name="Kind">What kind of item this is.</param>
/// <param name="CatalogItemId">Catalogue id; <see langword="null"/> for a custom line.</param>
/// <param name="Title">Line description as it should appear on the invoice.</param>
/// <param name="Quantity">Amount consumed, with its unit.</param>
/// <param name="UnitPrice">Price per unit at the time of consumption.</param>
/// <param name="ConsumedUtc">When it was consumed, UTC.</param>
/// <param name="RecordedByUserId">The staff member who recorded it.</param>
public sealed record StayItem(
    string ItemId,
    StayItemKind Kind,
    string? CatalogItemId,
    string Title,
    Quantity Quantity,
    Money UnitPrice,
    DateTimeOffset ConsumedUtc,
    string RecordedByUserId);
