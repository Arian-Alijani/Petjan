using Animora.Application.Abstractions;
using Animora.Application.Auditing;
using Animora.Application.Commands;
using Animora.Application.Ports;
using Animora.Application.Security;
using Animora.Application.Validation;
using Animora.Contracts.Wire.Commands;
using Animora.Domain.Boarding;
using Animora.Domain.Identity;
using Animora.Domain.Results;

namespace Animora.Application.UseCases.Boarding;

internal static class RoomRules
{
    public const int MaxCodeLength = 32;
    public const int MaxNameLength = 128;
    public const int MaxNotesLength = 1024;

    public static IReadOnlyList<ValidationFailure> Validate(string displayName, int capacity, string? notes)
    {
        var failures = new List<ValidationFailure>();
        if (Rules.Required(displayName, nameof(displayName)) is { } required) failures.Add(required);
        if (Rules.MaxLength(displayName, MaxNameLength, nameof(displayName)) is { } length) failures.Add(length);
        if (Rules.Range(capacity, Room.MinCapacity, Room.MaxCapacity, nameof(capacity)) is { } range) failures.Add(range);
        if (Rules.MaxLength(notes, MaxNotesLength, nameof(notes)) is { } notesLength) failures.Add(notesLength);
        return failures;
    }
}

/// <summary>Adds a boarding room (starts <see cref="RoomStatus.Available"/>). Returns the room id.</summary>
[RequiresPermission(PermissionCodes.SettingsWrite)]
[Auditable(AuditCategory.Configuration, CommandTypes.BoardingRoomCreate)]
public sealed record CreateRoomCommand(string Code, string DisplayName, RoomType RoomType, int Capacity, string? Notes)
    : ICommand<string>, IJournaledCommand
{
    public string CommandTypeName => CommandTypes.BoardingRoomCreate;
    public string AggregateTypeName => "Room";
    public string? AggregateIdOrNull => null;
}

public sealed class CreateRoomCommandValidator : IValidator<CreateRoomCommand>
{
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(CreateRoomCommand c, CancellationToken ct)
    {
        var failures = new List<ValidationFailure>(RoomRules.Validate(c.DisplayName, c.Capacity, c.Notes));
        if (Rules.Required(c.Code, nameof(c.Code)) is { } code) failures.Add(code);
        if (Rules.MaxLength(c.Code, RoomRules.MaxCodeLength, nameof(c.Code)) is { } codeLength) failures.Add(codeLength);
        if (Rules.ValidEnum(c.RoomType, nameof(c.RoomType)) is { } type) failures.Add(type);
        return ValueTask.FromResult<IReadOnlyList<ValidationFailure>>(failures);
    }
}

public sealed class CreateRoomCommandHandler(IRoomRepository rooms) : ICommandHandler<CreateRoomCommand, string>
{
    public async Task<Result<string>> HandleAsync(CreateRoomCommand c, CommandContext context, CancellationToken ct)
    {
        var code = c.Code.Trim();
        if (await rooms.CodeExistsAsync(code, null, ct)) return Result<string>.Fail(ErrorCodes.RoomCodeDuplicate);

        var created = Room.Create(context.TenantId, code, c.DisplayName, c.RoomType, c.Capacity, c.Notes);
        if (created.IsFailure) return Result<string>.Fail(created.Error, created.Detail);

        rooms.Add(created.Value!);
        return Result<string>.Ok(created.Value!.Id);
    }
}

/// <summary>Changes a room's name, capacity, operational status and notes (its code and type stay fixed).</summary>
[RequiresPermission(PermissionCodes.SettingsWrite)]
[Auditable(AuditCategory.Configuration, CommandTypes.BoardingRoomUpdate)]
public sealed record UpdateRoomCommand(string Id, string DisplayName, int Capacity, RoomStatus Status, string? Notes)
    : ICommand<Unit>, IJournaledCommand
{
    public string CommandTypeName => CommandTypes.BoardingRoomUpdate;
    public string AggregateTypeName => "Room";
    public string? AggregateIdOrNull => Id;
}

public sealed class UpdateRoomCommandValidator : IValidator<UpdateRoomCommand>
{
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(UpdateRoomCommand c, CancellationToken ct)
    {
        var failures = new List<ValidationFailure>(RoomRules.Validate(c.DisplayName, c.Capacity, c.Notes));
        if (Rules.Required(c.Id, nameof(c.Id)) is { } id) failures.Add(id);
        if (Rules.ValidEnum(c.Status, nameof(c.Status)) is { } status) failures.Add(status);
        return ValueTask.FromResult<IReadOnlyList<ValidationFailure>>(failures);
    }
}

public sealed class UpdateRoomCommandHandler(IRoomRepository rooms, IBoardingQueries queries)
    : ICommandHandler<UpdateRoomCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(UpdateRoomCommand c, CommandContext context, CancellationToken ct)
    {
        var room = await rooms.FindAsync(c.Id, ct);
        if (room is null) return Result<Unit>.Fail(ErrorCodes.RoomNotFound);

        // «Occupied» is derived from active stays, never stored by hand; a room holding patients can't be
        // taken out of service or shrunk below its current occupancy.
        var occupied = await queries.CountActiveStaysInRoomAsync(room.Id, ct);
        var status = c.Status == RoomStatus.Occupied ? RoomStatus.Available : c.Status;
        if (occupied > 0 && status is RoomStatus.Maintenance or RoomStatus.Retired)
            return Result<Unit>.Fail(ErrorCodes.RoomUnavailable);
        if (c.Capacity < occupied) return Result<Unit>.Fail(ErrorCodes.RoomAtCapacity);

        var named = room.Rename(c.DisplayName, c.Notes);
        if (named.IsFailure) return Result<Unit>.Fail(named.Error, named.Detail);
        var sized = room.SetCapacity(c.Capacity);
        if (sized.IsFailure) return Result<Unit>.Fail(sized.Error, sized.Detail);
        var moved = room.SetStatus(status);
        return moved.IsFailure ? Result<Unit>.Fail(moved.Error, moved.Detail) : Result<Unit>.Ok(Unit.Value);
    }
}
