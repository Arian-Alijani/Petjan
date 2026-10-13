using Animora.Domain.Primitives;
using Animora.Domain.Results;

namespace Animora.Domain.Boarding;

/// <summary>The kind of boarding room.</summary>
public enum RoomType
{
    /// <summary>A standard boarding room.</summary>
    Standard = 0,

    /// <summary>A premium room.</summary>
    Vip = 1,

    /// <summary>An isolation room for contagious patients.</summary>
    Isolation = 2,

    /// <summary>A cage.</summary>
    Cage = 3,

    /// <summary>A suite.</summary>
    Suite = 4,
}

/// <summary>Operational status of a room.</summary>
public enum RoomStatus
{
    /// <summary>Ready to take a patient.</summary>
    Available = 0,

    /// <summary>Currently holding a patient.</summary>
    Occupied = 1,

    /// <summary>Temporarily out of service.</summary>
    Maintenance = 2,

    /// <summary>Permanently withdrawn. Terminal — a retired room never returns to service.</summary>
    Retired = 3,
}

/// <summary>
/// A boarding room aggregate root: a bookable space with a type, a capacity and an operational
/// status.
/// </summary>
/// <remarks>
/// Capacity is bounded to [1, 10] — a room holds at least one and, in practice, at most ten patients.
/// The one-way status invariant is <see cref="RoomStatus.Retired"/> → <see cref="RoomStatus.Available"/>:
/// a retired room is permanently withdrawn and must never silently return to service
/// (<c>ROOM_UNAVAILABLE</c>). Every mutator returns <see cref="Result"/>.
/// </remarks>
public sealed class Room : AggregateRoot
{
    /// <summary>Minimum room capacity.</summary>
    public const int MinCapacity = 1;

    /// <summary>Maximum room capacity.</summary>
    public const int MaxCapacity = 10;

    /// <summary>EF Core materialization constructor. Not for business code.</summary>
    private Room()
    {
    }

    /// <summary>Short room code, e.g. <c>R-101</c>.</summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>Human-readable room name.</summary>
    public string DisplayName { get; private set; } = string.Empty;

    /// <summary>The kind of room.</summary>
    public RoomType Type { get; private set; }

    /// <summary>How many patients the room holds.</summary>
    public int Capacity { get; private set; }

    /// <summary>Current operational status.</summary>
    public RoomStatus Status { get; private set; } = RoomStatus.Available;

    /// <summary>Free-form notes.</summary>
    public string? Notes { get; private set; }

    /// <summary>Creates a room in <see cref="RoomStatus.Available"/>.</summary>
    public static Result<Room> Create(
        string tenantId,
        string code,
        string displayName,
        RoomType type,
        int capacity,
        string? notes = null,
        string? id = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        if (capacity is < MinCapacity or > MaxCapacity)
        {
            return Result<Room>.Fail(
                ErrorCodes.RoomCapacityInvalid,
                $"Capacity {capacity} must be in [{MinCapacity}, {MaxCapacity}].");
        }

        return new Room
        {
            Id = string.IsNullOrWhiteSpace(id) ? IdGenerator.NewId() : id,
            TenantId = tenantId,
            Code = code.Trim(),
            DisplayName = displayName.Trim(),
            Type = type,
            Capacity = capacity,
            Status = RoomStatus.Available,
            Notes = Clean(notes),
        };
    }

    /// <summary>Changes the capacity, keeping it within [1, 10].</summary>
    public Result SetCapacity(int capacity)
    {
        if (capacity is < MinCapacity or > MaxCapacity)
        {
            return Result.Fail(
                ErrorCodes.RoomCapacityInvalid,
                $"Capacity {capacity} must be in [{MinCapacity}, {MaxCapacity}].");
        }

        Capacity = capacity;
        return Result.Ok();
    }

    /// <summary>
    /// Changes the operational status. A retired room may never return to
    /// <see cref="RoomStatus.Available"/> (<c>ROOM_UNAVAILABLE</c>).
    /// </summary>
    public Result SetStatus(RoomStatus status)
    {
        if (Status == RoomStatus.Retired && status == RoomStatus.Available)
        {
            return Result.Fail(
                ErrorCodes.RoomUnavailable,
                $"Room '{Id}' is retired and cannot return to service.");
        }

        Status = status;
        return Result.Ok();
    }

    /// <summary>Updates the descriptive fields.</summary>
    public Result Rename(string displayName, string? notes = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        DisplayName = displayName.Trim();
        Notes = Clean(notes);
        return Result.Ok();
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
