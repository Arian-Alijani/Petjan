using System.Text.Json;
namespace Animora.Contracts.Wire.Sync.Contracts;

/// <summary>Explicit allowed replication fields; identity and bookkeeping are carried by SyncRecord.</summary>
public sealed record ReceptionQueueEntrySyncContract
{
    public string PetId { get; init; } = string.Empty;
    public string OwnerId { get; init; } = string.Empty;
    public string? AppointmentId { get; init; }
    public string? DoctorUserId { get; init; }
    public int State { get; init; }
    public int Source { get; init; }
    public int TicketNumber { get; init; }
    public DateTimeOffset EnqueuedUtc { get; init; }
    public DateTimeOffset? CalledUtc { get; init; }
    public DateTimeOffset? ClosedUtc { get; init; }
    public int Priority { get; init; }
    public string? Note { get; init; }
    public string? VisitId { get; init; }
}
