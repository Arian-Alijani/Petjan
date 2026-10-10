using System.Text.Json;
namespace Animora.Contracts.Wire.Sync.Contracts;

/// <summary>Explicit allowed replication fields; identity and bookkeeping are carried by SyncRecord.</summary>
public sealed record VaccinationRecordSyncContract
{
    public string PetId { get; init; } = string.Empty;
    public string VaccineCode { get; init; } = string.Empty;
    public string VaccineName { get; init; } = string.Empty;
    public int DoseNumber { get; init; }
    public int? TotalDoses { get; init; }
    public DateTimeOffset AdministeredUtc { get; init; }
    public string AdministeredByUserId { get; init; } = string.Empty;
    public string? BatchNumber { get; init; }
    public DateTimeOffset? NextDueUtc { get; init; }
    public int State { get; init; }
    public string? VisitId { get; init; }
    public string? Notes { get; init; }
}
