using System.Text.Json;
namespace Animora.Contracts.Wire.Sync.Contracts;

/// <summary>Explicit allowed replication fields; identity and bookkeeping are carried by SyncRecord.</summary>
public sealed record TenantSyncContract
{
    public string DisplayName { get; init; } = string.Empty;
    public string? LegalName { get; init; }
    public string Timezone { get; init; } = string.Empty;
    public string DefaultCurrency { get; init; } = string.Empty;
    public int Status { get; init; }
    public string? Code { get; init; }
}
