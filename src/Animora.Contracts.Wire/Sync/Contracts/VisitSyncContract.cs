using System.Text.Json;
namespace Animora.Contracts.Wire.Sync.Contracts;

/// <summary>Explicit allowed replication fields; identity and bookkeeping are carried by SyncRecord.</summary>
public sealed record VisitSyncContract
{
    public string PetId { get; init; } = string.Empty;
    public string OwnerId { get; init; } = string.Empty;
    public string? AppointmentId { get; init; }
    public string DoctorUserId { get; init; } = string.Empty;
    public int State { get; init; }
    public int Reason { get; init; }
    public string? ChiefComplaint { get; init; }
    public DateTimeOffset StartedUtc { get; init; }
    public DateTimeOffset? CompletedUtc { get; init; }
    public JsonElement? Vitals { get; init; }
    public string? Diagnosis { get; init; }
    public string? TreatmentPlan { get; init; }
    public string? DoctorNotes { get; init; }
    public string? InvoiceId { get; init; }
    public string? CancellationReason { get; init; }
    public JsonElement Prescriptions { get; init; }
    public JsonElement ServiceLines { get; init; }
    public JsonElement BillingHandoffs { get; init; }
}
