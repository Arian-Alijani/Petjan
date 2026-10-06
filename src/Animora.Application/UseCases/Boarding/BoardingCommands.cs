using System.Text.Json;
using Animora.Application.Abstractions;
using Animora.Application.Auditing;
using Animora.Application.Commands;
using Animora.Application.Ports;
using Animora.Application.Security;
using Animora.Application.Time;
using Animora.Application.Validation;
using Animora.Contracts.Wire.Commands;
using Animora.Domain.Boarding;
using Animora.Domain.Identity;
using Animora.Domain.Patients;
using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Domain.Time;
using Animora.Domain.ValueObjects;

namespace Animora.Application.UseCases.Boarding;

[RequiresPermission(PermissionCodes.BoardingManage)]
[Auditable(AuditCategory.Clinical, CommandTypes.BoardingStayCreate)]
public sealed record CreateBoardingStayCommand(string PetId, string RoomId, DateOnly CheckIn,
    DateOnly CheckOut, long DailyRateMinor, string? CareNote,
    StayPaymentMode PaymentMode = StayPaymentMode.Postpaid,
    WalkInBoarder? WalkIn = null) : ICommand<string>, IJournaledCommand
{
    public string CommandTypeName => CommandTypes.BoardingStayCreate;
    public string AggregateTypeName => "BoardingStay";
    public string? AggregateIdOrNull => null;
}
/// <summary>
/// Identity of a boarder who has no patient file yet (manual admission). The handler finds or
/// creates the owner (by phone) and the pet (by owner + name), so no typed field is dropped.
/// </summary>
public sealed record WalkInBoarder(string PetName, PetSpecies Species, string? Breed,
    string OwnerName, string OwnerPhone);

public sealed class CreateBoardingStayCommandValidator : IValidator<CreateBoardingStayCommand>
{
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(CreateBoardingStayCommand c, CancellationToken ct)
    {
        var failures = new List<ValidationFailure>();
        if (c.WalkIn is null)
        {
            if (Rules.Required(c.PetId, nameof(c.PetId)) is { } pet) failures.Add(pet);
        }
        else
        {
            if (Rules.Required(c.WalkIn.PetName, nameof(c.WalkIn.PetName)) is { } name) failures.Add(name);
            if (Rules.Required(c.WalkIn.OwnerName, nameof(c.WalkIn.OwnerName)) is { } owner) failures.Add(owner);
            if (Rules.Required(c.WalkIn.OwnerPhone, nameof(c.WalkIn.OwnerPhone)) is { } phone) failures.Add(phone);
        }
        if (Rules.Required(c.RoomId, nameof(c.RoomId)) is { } room) failures.Add(room);
        return ValueTask.FromResult<IReadOnlyList<ValidationFailure>>(failures);
    }
}
public sealed class CreateBoardingStayCommandHandler(IBoardingRepository repository,
    IBoardingQueries queries, IOwnerRepository owners, IPetRepository pets,
    IClinicTimeZoneProvider zones, IClock clock, IBoardingBillingHandoffWriter handoffs)
    : ICommandHandler<CreateBoardingStayCommand, string>
{
    public async Task<Result<string>> HandleAsync(CreateBoardingStayCommand c, CommandContext context, CancellationToken ct)
    {
        if (c.CheckOut <= c.CheckIn || c.DailyRateMinor < 0)
            return Result<string>.Fail(ErrorCodes.StayDatesInvalid);
        var room = await queries.FindRoomAsync(c.RoomId, ct);
        if (room is null || room.Status is RoomStatus.Maintenance or RoomStatus.Retired)
            return Result<string>.Fail(ErrorCodes.RoomUnavailable);
        if (await queries.CountActiveStaysInRoomAsync(room.Id, ct) >= room.Capacity)
            return Result<string>.Fail(ErrorCodes.RoomAtCapacity);
        string petId, ownerId;
        if (c.WalkIn is { } walkIn)
        {
            (petId, ownerId) = await ResolveWalkInAsync(walkIn, context, ct);
        }
        else
        {
            var pet = await queries.FindPetAsync(c.PetId, ct);
            if (pet is null) return Result<string>.Fail(ErrorCodes.PetNotFound);
            (petId, ownerId) = (pet.Id, pet.OwnerId);
        }
        var tz = zones.GetClinicTimeZone();
        var from = new DateTimeOffset(ClinicCalendar.ToUtcTicks(c.CheckIn, 0, tz), TimeSpan.Zero);
        var until = new DateTimeOffset(ClinicCalendar.ToUtcTicks(c.CheckOut, 0, tz), TimeSpan.Zero);
        var created = BoardingStay.Reserve(context.TenantId, petId, ownerId, room.Id,
            from, until, Money.Irt(c.DailyRateMinor), c.CareNote, paymentMode: c.PaymentMode);
        if (created.IsFailure) return Result<string>.Fail(created.Error, created.Detail);
        var stay = created.Value!;
        var activated = stay.Activate(clock);
        if (activated.IsFailure) return Result<string>.Fail(activated.Error, activated.Detail);
        repository.Add(stay);

        // Prepaid: raise the room charge to the cashier immediately so the clinic collects it before the
        // stay. The stay stays Active and editable — any items staff consume later are billed on a second
        // invoice at checkout. The payload carries no items and a room-only total; Sales turns it into the
        // prepaid room invoice, and posting it records it on the stay (SettlePrepaidRoom) without checkout.
        if (c.PaymentMode == StayPaymentMode.Prepaid)
        {
            var plannedNights = StayChargeCalculator.NightsBetween(from, until, tz);
            var roomCharge = Money.Irt(c.DailyRateMinor) * plannedNights;
            var payload = JsonSerializer.Serialize(new
            {
                RoomRateMinor = c.DailyRateMinor,
                TotalMinor = roomCharge.Minor,
                Currency = roomCharge.Currency,
                Items = Array.Empty<object>(),
            });
            handoffs.Add(context.TenantId, stay.Id, ownerId, petId, clock.UtcNow, payload,
                BoardingHandoffKind.Room);
        }

        return Result<string>.Ok(stay.Id);
    }

    // Reuses an existing owner (same normalized phone) and pet (same owner + name) so a repeat
    // walk-in never duplicates the file; only creates what is missing.
    private async Task<(string PetId, string OwnerId)> ResolveWalkInAsync(WalkInBoarder walkIn,
        CommandContext context, CancellationToken ct)
    {
        var owner = await owners.FindByPhoneAsync(Owner.NormalizePhone(walkIn.OwnerPhone), ct);
        if (owner is null)
        {
            owner = Owner.Register(context.TenantId, walkIn.OwnerName, walkIn.OwnerPhone, clock);
            owners.Add(owner);
        }
        var petName = walkIn.PetName.Trim();
        var pet = await pets.FindByOwnerAndNameAsync(owner.Id, petName, ct);
        if (pet is null)
        {
            var breed = string.IsNullOrWhiteSpace(walkIn.Breed) ? null : walkIn.Breed.Trim();
            pet = Pet.Register(context.TenantId, owner.Id, petName, walkIn.Species, clock,
                PetGender.Unknown, null, breed);
            pets.Add(pet);
        }
        return (pet.Id, owner.Id);
    }
}

[RequiresPermission(PermissionCodes.BoardingManage)]
[Auditable(AuditCategory.Clinical, CommandTypes.BoardingItemAdd)]
public sealed record AddStayItemCommand(string StayId, string CatalogItemId, int Quantity)
    : ICommand<Unit>, IJournaledCommand
{
    public string CommandTypeName => CommandTypes.BoardingItemAdd;
    public string AggregateTypeName => "BoardingStay";
    public string? AggregateIdOrNull => StayId;
}
public sealed class AddStayItemCommandHandler(IBoardingRepository repository, IBoardingQueries queries, IClock clock)
    : ICommandHandler<AddStayItemCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(AddStayItemCommand c, CommandContext context, CancellationToken ct)
    {
        var stay = await repository.FindAsync(c.StayId, ct);
        if (stay is null) return Result<Unit>.Fail(ErrorCodes.StayNotFound);
        if (stay.State != StayState.Active || c.Quantity < 1) return Result<Unit>.Fail(ErrorCodes.StayNotEditable);
        var selected = await queries.FindSelectableAsync(c.CatalogItemId, ct);
        if (selected is null) return Result<Unit>.Fail(ErrorCodes.NotFound);
        var kind = selected.Source switch
        {
            "BoardingService" => StayItemKind.BoardingService,
            "ClinicService" => StayItemKind.ClinicService,
            _ => StayItemKind.Product,
        };
        var item = new StayItem(IdGenerator.NewId(clock.UtcNow), kind, selected.Id, selected.Title,
            new Quantity(c.Quantity, "PCS"), Money.Irt(selected.PriceMinor), clock.UtcNow, context.UserId);
        var result = stay.AddItem(item);
        return result.IsFailure ? Result<Unit>.Fail(ErrorCodes.StayNotEditable) : Result<Unit>.Ok(Unit.Value);
    }
}

[RequiresPermission(PermissionCodes.BoardingManage)]
[Auditable(AuditCategory.Clinical, CommandTypes.BoardingItemRemove)]
public sealed record RemoveStayItemCommand(string StayId, int Index, string ExpectedItemId)
    : ICommand<Unit>, IJournaledCommand
{
    public string CommandTypeName => CommandTypes.BoardingItemRemove;
    public string AggregateTypeName => "BoardingStay";
    public string? AggregateIdOrNull => StayId;
}
public sealed class RemoveStayItemCommandHandler(IBoardingRepository repository)
    : ICommandHandler<RemoveStayItemCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(RemoveStayItemCommand c, CommandContext context, CancellationToken ct)
    {
        var stay = await repository.FindAsync(c.StayId, ct);
        if (stay is null) return Result<Unit>.Fail(ErrorCodes.StayNotFound);
        if (stay.State != StayState.Active) return Result<Unit>.Fail(ErrorCodes.StayNotEditable);
        var current = stay.Items.OrderBy(i => i.ConsumedUtc).ThenBy(i => i.ItemId, StringComparer.Ordinal)
            .Skip(Math.Max(0, c.Index)).FirstOrDefault();
        if (c.Index < 0 || current is null || current.ItemId != c.ExpectedItemId)
            return Result<Unit>.Fail(ErrorCodes.StayItemIndexStale);
        var result = stay.RemoveItem(current.ItemId);
        return result.IsFailure ? Result<Unit>.Fail(result.Error, result.Detail) : Result<Unit>.Ok(Unit.Value);
    }
}

[RequiresPermission(PermissionCodes.BoardingManage)]
[Auditable(AuditCategory.Clinical, CommandTypes.BoardingStayTransfer)]
public sealed record TransferStayRoomCommand(string StayId, string? TargetRoomId = null)
    : ICommand<Unit>, IJournaledCommand
{
    public string CommandTypeName => CommandTypes.BoardingStayTransfer;
    public string AggregateTypeName => "BoardingStay";
    public string? AggregateIdOrNull => StayId;
}
public sealed class TransferStayRoomCommandHandler(IBoardingRepository repository, IBoardingQueries queries,
    IClock clock) : ICommandHandler<TransferStayRoomCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(TransferStayRoomCommand c, CommandContext context, CancellationToken ct)
    {
        var stay = await repository.FindAsync(c.StayId, ct);
        if (stay is null) return Result<Unit>.Fail(ErrorCodes.StayNotFound);
        if (stay.State != StayState.Active) return Result<Unit>.Fail(ErrorCodes.StayNotEditable);
        var current = await queries.FindRoomAsync(stay.RoomId, ct);
        var target = c.TargetRoomId is null ? null : await queries.FindRoomAsync(c.TargetRoomId, ct);
        if (c.TargetRoomId is null)
        {
            var rooms = await queries.GetRoomsAsync(ct);
            target = rooms.Where(r => r.Room.Type == current?.Type && r.Room.Id != stay.RoomId &&
                r.Room.Status is RoomStatus.Available or RoomStatus.Occupied && r.Occupied < r.Room.Capacity)
                .Select(r => r.Room).FirstOrDefault();
            if (target is null) return Result<Unit>.Fail(ErrorCodes.RoomNoneAvailable);
        }
        if (target is null || target.Status is RoomStatus.Maintenance or RoomStatus.Retired || target.Id == stay.RoomId)
            return Result<Unit>.Fail(ErrorCodes.RoomUnavailable);
        if (await queries.CountActiveStaysInRoomAsync(target.Id, ct) >= target.Capacity)
            return Result<Unit>.Fail(ErrorCodes.RoomAtCapacity);
        var moved = stay.TransferRoom(target.Id, "Transfer", clock);
        return moved.IsFailure ? Result<Unit>.Fail(moved.Error, moved.Detail) : Result<Unit>.Ok(Unit.Value);
    }
}

[RequiresPermission(PermissionCodes.BoardingManage)]
[Auditable(AuditCategory.Clinical, CommandTypes.BoardingStayReschedule)]
public sealed record RescheduleStayCheckoutCommand(string StayId, DateOnly NewCheckOut)
    : ICommand<Unit>, IJournaledCommand
{
    public string CommandTypeName => CommandTypes.BoardingStayReschedule;
    public string AggregateTypeName => "BoardingStay";
    public string? AggregateIdOrNull => StayId;
}
public sealed class RescheduleStayCheckoutCommandValidator : IValidator<RescheduleStayCheckoutCommand>
{
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(RescheduleStayCheckoutCommand c, CancellationToken ct)
    {
        var failures = new List<ValidationFailure>();
        if (Rules.Required(c.StayId, nameof(c.StayId)) is { } id) failures.Add(id);
        return ValueTask.FromResult<IReadOnlyList<ValidationFailure>>(failures);
    }
}
public sealed class RescheduleStayCheckoutCommandHandler(IBoardingRepository repository,
    IClinicTimeZoneProvider zones) : ICommandHandler<RescheduleStayCheckoutCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(RescheduleStayCheckoutCommand c, CommandContext context, CancellationToken ct)
    {
        var stay = await repository.FindAsync(c.StayId, ct);
        if (stay is null) return Result<Unit>.Fail(ErrorCodes.StayNotFound);
        var tz = zones.GetClinicTimeZone();
        var until = new DateTimeOffset(ClinicCalendar.ToUtcTicks(c.NewCheckOut, 0, tz), TimeSpan.Zero);
        var moved = stay.RescheduleCheckout(until);
        return moved.IsFailure ? Result<Unit>.Fail(moved.Error, moved.Detail) : Result<Unit>.Ok(Unit.Value);
    }
}

[RequiresPermission(PermissionCodes.BoardingCheckout)]
[Auditable(AuditCategory.Clinical, CommandTypes.BoardingStayCheckout)]
public sealed record CheckoutStayCommand(string StayId) : ICommand<Unit>, IJournaledCommand
{
    public string CommandTypeName => CommandTypes.BoardingStayCheckout;
    public string AggregateTypeName => "BoardingStay";
    public string? AggregateIdOrNull => StayId;
}
public sealed class CheckoutStayCommandHandler(IBoardingRepository repository, IBoardingQueries queries,
    IBoardingBillingHandoffWriter handoffs, IClinicTimeZoneProvider zones, IClock clock)
    : ICommandHandler<CheckoutStayCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(CheckoutStayCommand c, CommandContext context, CancellationToken ct)
    {
        var stay = await repository.FindAsync(c.StayId, ct);
        if (stay is null) return Result<Unit>.Fail(ErrorCodes.StayNotFound);
        if (stay.State != StayState.Active) return Result<Unit>.Fail(ErrorCodes.StayAlreadyCheckedOut);
        // At most one open (Ready) billing handoff per stay: the posting path resolves handoffs by stay id.
        // A prepaid stay whose room invoice is still unposted must be settled at the cashier first.
        if (await queries.HasReadyHandoffAsync(stay.Id, ct))
            return Result<Unit>.Fail(ErrorCodes.StayPrepaidInvoicePending);
        var now = clock.UtcNow;
        var tz = zones.GetClinicTimeZone();
        var services = await queries.GetServicesAsync(ct);

        // A prepaid stay already paid its room in advance, so the checkout bill is the consumed items
        // only. A postpaid stay bills room + items in one go (unchanged).
        if (stay.IsRoomPrepaid)
        {
            // Nights stayed beyond what the prepaid room invoice covered (the check-out date was extended,
            // or the pet overstayed) are billed on the second invoice; staying fewer nights is not refunded.
            var elapsedRoom = stay.DailyRate * StayChargeCalculator.NightsBetween(stay.CheckInUtc, now, tz);
            var extraRoomMinor = Math.Max(0L, elapsedRoom.Minor - (stay.PrepaidRoomChargeMinor ?? 0L));

            // Nothing extra was consumed — there is no second invoice to raise; just close the stay.
            if (stay.Items.Count == 0 && extraRoomMinor == 0)
            {
                var done = stay.CompletePrepaidCheckoutWithoutInvoice(clock);
                return done.IsFailure ? Result<Unit>.Fail(done.Error, done.Detail) : Result<Unit>.Ok(Unit.Value);
            }

            var itemsCharge = StayChargeCalculator.ItemsOnly(stay, services, now, tz);
            var secondInvoiceTotal = itemsCharge + Money.Irt(extraRoomMinor);
            var requestedPrepaid = stay.RequestCheckout(clock);
            if (requestedPrepaid.IsFailure) return Result<Unit>.Fail(requestedPrepaid.Error, requestedPrepaid.Detail);
            var frozenPrepaid = stay.FreezeTotalForCheckout(secondInvoiceTotal);
            if (frozenPrepaid.IsFailure) return Result<Unit>.Fail(frozenPrepaid.Error, frozenPrepaid.Detail);
            // RoomRateMinor 0 and the flag tell Sales the room was prepaid: the second invoice bills the items
            // plus only the ExtraRoomMinor nights beyond the prepaid ones (usually 0 → no room line at all).
            var prepaidPayload = JsonSerializer.Serialize(new
            {
                RoomRateMinor = 0L,
                RoomPrepaid = true,
                PrepaidRoomChargeMinor = stay.PrepaidRoomChargeMinor ?? 0L,
                PrepaidInvoiceId = stay.PrepaidInvoiceId,
                ExtraRoomMinor = extraRoomMinor,
                TotalMinor = secondInvoiceTotal.Minor,
                Currency = secondInvoiceTotal.Currency,
                Items = stay.Items.OrderBy(i => i.ConsumedUtc).ThenBy(i => i.ItemId)
                    .Select(i => new { i.ItemId, i.Kind, i.CatalogItemId, i.Title,
                        Quantity = i.Quantity.Value, UnitPriceMinor = i.UnitPrice.Minor }),
            });
            handoffs.Add(context.TenantId, stay.Id, stay.OwnerId, stay.PetId, now, prepaidPayload);
            return Result<Unit>.Ok(Unit.Value);
        }

        var charge = StayChargeCalculator.Accrued(stay, services, now, tz);
        var requested = stay.RequestCheckout(clock);
        if (requested.IsFailure) return Result<Unit>.Fail(requested.Error, requested.Detail);
        var frozen = stay.FreezeTotalForCheckout(charge);
        if (frozen.IsFailure) return Result<Unit>.Fail(frozen.Error, frozen.Detail);
        var payload = JsonSerializer.Serialize(new
        {
            RoomRateMinor = stay.DailyRate.Minor,
            TotalMinor = charge.Minor,
            Currency = charge.Currency,
            Items = stay.Items.OrderBy(i => i.ConsumedUtc).ThenBy(i => i.ItemId)
                .Select(i => new { i.ItemId, i.Kind, i.CatalogItemId, i.Title,
                    Quantity = i.Quantity.Value, UnitPriceMinor = i.UnitPrice.Minor }),
        });
        handoffs.Add(context.TenantId, stay.Id, stay.OwnerId, stay.PetId, now, payload);
        return Result<Unit>.Ok(Unit.Value);
    }
}
