using System.Text.Json;
namespace Animora.Contracts.Wire.Sync.Contracts;

/// <summary>Explicit allowed replication fields; identity and bookkeeping are carried by SyncRecord.</summary>
public sealed record AppointmentSyncContract
{
    public string PetId { get; init; } = string.Empty;
    public string OwnerId { get; init; } = string.Empty;
    public string? DoctorUserId { get; init; }
    public string AppointmentTypeCode { get; init; } = string.Empty;
    public DateTimeOffset StartUtc { get; init; }
    public int DurationMinutes { get; init; }
    public int State { get; init; }
    public int Source { get; init; }
    public string? Note { get; init; }
    public string? VisitId { get; init; }
    public string? CancelReason { get; init; }
    public DateTimeOffset? CheckedInUtc { get; init; }
    public string CreatedByUserId { get; init; } = string.Empty;
    public int RescheduleCount { get; init; }
}
