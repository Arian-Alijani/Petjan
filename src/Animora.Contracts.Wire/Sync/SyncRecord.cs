using System.Text.Json;
namespace Animora.Contracts.Wire.Sync;

public sealed record SyncRecord(string Entity, string EntityId, char Op, string? Version, DateTimeOffset? UpdatedAt, bool Deleted, JsonElement? Payload);
