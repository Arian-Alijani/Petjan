using Animora.Domain.ValueObjects;

namespace Animora.Domain.Boarding;

/// <summary>Derives the stay's charge at an instant without persisting an accruing balance.</summary>
public static class StayChargeCalculator
{
    public static int NightsBetween(DateTimeOffset start, DateTimeOffset end, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);
        var startDay = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(start, timeZone).DateTime);
        var endDay = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(end, timeZone).DateTime);
        return Math.Max(1, endDay.DayNumber - startDay.DayNumber);
    }

    public static Money Accrued(
        BoardingStay stay,
        IReadOnlyList<BoardingServiceDef> services,
        DateTimeOffset asOf,
        TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(stay);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(timeZone);

        var end = stay.ActualCheckOutUtc is { } checkout && checkout < asOf ? checkout : asOf;
        var nights = NightsBetween(stay.CheckInUtc, end, timeZone);
        var total = stay.DailyRate * nights;
        var billingById = services.ToDictionary(service => service.Id, service => service.Billing);
        foreach (var item in stay.Items)
        {
            if (item.Kind == StayItemKind.BoardingService && item.CatalogItemId is { } id &&
                billingById.TryGetValue(id, out var billing) && billing == BoardingServiceBilling.PerDay)
            {
                total += item.UnitPrice * nights;
            }
            else
            {
                // Money.ApplyPercent is the product's only fractional-money rounding boundary.
                total += item.UnitPrice.ApplyPercent(item.Quantity.Value * 100m);
            }
        }

        return total;
    }
}
