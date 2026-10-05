using System.Globalization;
using Animora.Application.Time;
using Animora.Application.UseCases.Boarding.ReadModels;
using Animora.Contracts.Boarding;
using Animora.Contracts.Identity;
using Animora.Domain.Boarding;
using Animora.Domain.Patients;
using ContractStay = Animora.Contracts.Boarding.BoardingStay;

namespace Animora.Client.Services.Boarding;

internal static class BoardingMapping
{
    internal static BoardingSpecies Species(string species) => species switch
    {
        "Dog" => BoardingSpecies.Dog,
        "Cat" => BoardingSpecies.Cat,
        "Bird" => BoardingSpecies.Bird,
        "Rodent" => BoardingSpecies.Rodent,
        "Rabbit" => BoardingSpecies.Rabbit,
        _ => BoardingSpecies.Other, // Reptiles and other clinic species have no boarding equivalent.
    };

    internal static PetSpecies PetSpecies(BoardingSpecies species) => species switch
    {
        BoardingSpecies.Dog => Domain.Patients.PetSpecies.Dog,
        BoardingSpecies.Cat => Domain.Patients.PetSpecies.Cat,
        BoardingSpecies.Bird => Domain.Patients.PetSpecies.Bird,
        BoardingSpecies.Rabbit => Domain.Patients.PetSpecies.Rabbit,
        BoardingSpecies.Rodent => Domain.Patients.PetSpecies.Rodent,
        _ => Domain.Patients.PetSpecies.Other,
    };

    /// <summary>The clinic species a boarding species filter stands for ("Other" covers reptiles too).</summary>
    internal static IReadOnlyList<PetSpecies> PetSpeciesFilter(BoardingSpecies? species) => species switch
    {
        null => [],
        BoardingSpecies.Other => [Domain.Patients.PetSpecies.Other, Domain.Patients.PetSpecies.Reptile],
        var s => [PetSpecies(s.Value)],
    };

    internal static ContractStay ToContract(this StayListItemReadModel row,
        IReadOnlyList<BoardingServiceDef> services, DateTimeOffset now, TimeZoneInfo tz)
    {
        var stay = row.Stay;
        var end = stay.ActualCheckOutUtc ?? (stay.State == StayState.Active ? now :
            stay.PlannedCheckOutUtc < now ? stay.PlannedCheckOutUtc : now);
        var nights = StayChargeCalculator.NightsBetween(stay.CheckInUtc, end, tz);
        var charge = StayChargeCalculator.Accrued(stay, services, end, tz);
        return new ContractStay(stay.Id, row.PetName, Species(row.Species), row.Breed,
            row.OwnerName, row.OwnerPhone, stay.PetId, row.RoomLabel,
            ClinicCalendar.LocalDay(stay.CheckInUtc.UtcTicks, tz),
            ClinicCalendar.LocalDay(stay.PlannedCheckOutUtc.UtcTicks, tz), stay.DailyRate.Minor,
            stay.PaymentMode == StayPaymentMode.Prepaid ? BoardingPaymentMode.Prepaid : BoardingPaymentMode.Postpaid,
            stay.State == StayState.Active ? BoardingStatus.Active : BoardingStatus.Done,
            "a", stay.State >= StayState.ReadyForInvoice, stay.CareNote ?? "",
            charge.Minor - (stay.DailyRate * nights).Minor);
    }

    internal static StayDetail ToContract(this StayDetailReadModel row,
        IReadOnlyList<BoardingServiceDef> services, DateTimeOffset now, TimeZoneInfo tz) =>
        new(row.Header.ToContract(services, now, tz), row.Items.Select(item =>
            new BoardingStayItem(item.CatalogItemId ?? item.ItemId,
                item.Kind == StayItemKind.Product ? BoardingItemKind.Item : BoardingItemKind.Service,
                item.Title, (int)item.Quantity.Value, item.UnitPrice.Minor,
                ClinicCalendar.LocalDay(item.ConsumedUtc.UtcTicks, tz))).ToArray());

    internal static RoomInfo ToContract(this RoomOccupancyReadModel row) =>
        new(row.Room.Id, row.Room.Code,
            // Isolation and Cage fit Standard; Suite fits Vip in the two-value UI contract.
            row.Room.Type is RoomType.Vip or RoomType.Suite ? BoardingRoomType.Vip : BoardingRoomType.Standard,
            BoardingSpecies.Other, row.Room.Capacity, row.Occupied > 0);

    internal static BoardingKnownPet ToContract(this BoardingKnownPetReadModel row) =>
        new(row.Name, Species(row.Species), row.Breed, row.OwnerName, row.OwnerPhone, row.Id);

    internal static BoardingHistoryEntry ToContract(this BoardingHistoryRowReadModel row, TimeZoneInfo tz)
    {
        var local = TimeZoneInfo.ConvertTime(row.OccurredUtc, tz).DateTime;
        var calendar = new PersianCalendar();
        var date = $"{calendar.GetYear(local):0000}/{calendar.GetMonth(local):00}/{calendar.GetDayOfMonth(local):00}";
        var kind = row.Kind == "CheckOut" ? BoardingHistoryKind.CheckOut : BoardingHistoryKind.CheckIn;
        return new BoardingHistoryEntry(row.Id, kind, row.Kind, row.StayId, row.PetName,
            Species(row.Species), row.OwnerName, row.RoomLabel, date,
            local.ToString("HH:mm", CultureInfo.InvariantCulture), 0, row.AmountMinor,
            row.AmountMinor > 0 ? 1 : 0, "", ActorRef.System);
    }
}
