using System.Text.Json;
namespace Animora.Contracts.Wire.Sync.Contracts;

/// <summary>Explicit allowed replication fields; identity and bookkeeping are carried by SyncRecord.</summary>
public sealed record PetSyncContract
{
    public string OwnerId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int Species { get; init; }
    public string? Breed { get; init; }
    public int Gender { get; init; }
    public DateTimeOffset? BirthDateUtc { get; init; }
    public bool IsNeutered { get; init; }
    public string? Color { get; init; }
    public string? MicrochipCode { get; init; }
    public DateTimeOffset? MicrochipImplantUtc { get; init; }
    public int Status { get; init; }
    public string? AllergyNotes { get; init; }
    public string? ChronicNotes { get; init; }
    public DateTimeOffset RegisteredUtc { get; init; }
}
