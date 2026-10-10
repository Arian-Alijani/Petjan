using System.Text.Json;
namespace Animora.Contracts.Wire.Sync;

public sealed record SyncManifest(int ContractVersion, long Epoch, long Generation, IReadOnlyList<string> ProjectionOrder, int PageTargetBytes);
