using System.Text.Json;
namespace Animora.Contracts.Wire.Sync;

public sealed record OpenBatchRequest(long Epoch, long FromSequence);
