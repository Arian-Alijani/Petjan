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
using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Domain.Time;
using Animora.Domain.ValueObjects;

namespace Animora.Application.UseCases.Boarding;

[RequiresPermission(PermissionCodes.BoardingManage)]
[Auditable(AuditCategory.Clinical, CommandTypes.BoardingStayCreate)]
public sealed record CreateBoardingStayCommand(string PetId, string RoomId, DateOnly CheckIn,
    DateOnly CheckOut, long DailyRateMinor, string? CareNote) : ICommand<string>, IJournaledCommand
{
    public string CommandTypeName => CommandTypes.BoardingStayCreate;
    public string AggregateTypeName => "BoardingStay";
    public string? AggregateIdOrNull => null;
}
public sealed class CreateBoardingStayCommandValidator : IValidator<CreateBoardingStayCommand>
{
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(CreateBoardingStayCommand c, CancellationToken ct)
    {
        var failures = new List<ValidationFailure>();
        if (Rules.Required(c.PetId, nameof(c.PetId)) is { } pet) failures.Add(pet);
        if (Rules.Required(c.RoomId, nameof(c.RoomId)) is { } room) failures.Add(room);
        return ValueTask.FromResult<IReadOnlyList<ValidationFailure>>(failures);
    }
}
public sealed class CreateBoardingStayCommandHandler(IBoardingRepository repository,
    IBoardingQueries queries, IClinicTimeZoneProvider zones, IClock clock)
    : ICommandHandler<CreateBoardingStayCommand, string>
{
    public async Task<Result<string>> HandleAsync(CreateBoardingStayCommand c, CommandContext context, CancellationToken ct)
    {
        if (c.CheckOut <= c.CheckIn || c.DailyRateMinor < 0)
            return Result<string>.Fail(ErrorCodes.StayDatesInvalid);
        var pet = await queries.FindPetAsync(c.PetId, ct);
        if (pet is null) return Result<string>.Fail(ErrorCodes.PetNotFound);
        var room = await queries.FindRoomAsync(c.RoomId, ct);
        if (room is null || room.Status is RoomStatus.Maintenance or RoomStatus.Retired)
            return Result<string>.Fail(ErrorCodes.RoomUnavailable);
        if (await queries.CountActiveStaysInRoomAsync(room.Id, ct) >= room.Capacity)
            return Result<string>.Fail(ErrorCodes.RoomAtCapacity);
        var tz = zones.GetClinicTimeZone();
        var from = new DateTimeOffset(ClinicCalendar.ToUtcTicks(c.CheckIn, 0, tz), TimeSpan.Zero);
        var until = new DateTimeOffset(ClinicCalendar.ToUtcTicks(c.CheckOut, 0, tz), TimeSpan.Zero);
        var created = BoardingStay.Reserve(context.TenantId, pet.Id, pet.OwnerId, room.Id,
            from, until, Money.Irt(c.DailyRateMinor), c.CareNote);
        if (created.IsFailure) return Result<string>.Fail(created.Error, created.Detail);
        var stay = created.Value!;
        var activated = stay.Activate(clock);
        if (activated.IsFailure) return Result<string>.Fail(activated.Error, activated.Detail);
        repository.Add(stay);
        return Result<string>.Ok(stay.Id);
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
        var now = clock.UtcNow;
        var services = await queries.GetServicesAsync(ct);
        var charge = StayChargeCalculator.Accrued(stay, services, now, zones.GetClinicTimeZone());
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
