using System.Text.Json;
namespace Animora.Contracts.Wire.Sync.Contracts;

/// <summary>Explicit allowed replication fields; identity and bookkeeping are carried by SyncRecord.</summary>
public sealed record TreatmentPlanSyncContract
{
    public string PetId { get; init; } = string.Empty;
    public int ItemType { get; init; }
    public int Kind { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? VaccineCode { get; init; }
    public string? Dosage { get; init; }
    public string? Notes { get; init; }
    public bool NotifyOwner { get; init; }
    public int ScheduleType { get; init; }
    public string CreatedByUserId { get; init; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; init; }
    public int State { get; init; }
    public JsonElement Schedule { get; init; }
}
