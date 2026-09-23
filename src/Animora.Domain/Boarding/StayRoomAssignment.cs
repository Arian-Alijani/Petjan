namespace Animora.Domain.Boarding;

/// <summary>
/// One entry in a stay's room history: the pet occupied <paramref name="RoomId"/> from
/// <paramref name="FromUtc"/> until <paramref name="ToUtc"/> (open while current). A transfer closes
/// the current assignment and appends a new one, so the full room timeline of a stay is preserved.
/// </summary>
/// <param name="RoomId">The room occupied during this span.</param>
/// <param name="FromUtc">When the pet entered this room, UTC.</param>
/// <param name="ToUtc">When it left this room, UTC; <see langword="null"/> while current.</param>
/// <param name="Reason">Why the pet moved into this room; <see langword="null"/> for the first room.</param>
public sealed record StayRoomAssignment(
    string RoomId,
    DateTimeOffset FromUtc,
    DateTimeOffset? ToUtc,
    string? Reason);
