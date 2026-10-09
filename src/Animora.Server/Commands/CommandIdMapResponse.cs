namespace Animora.Server.Commands;

internal sealed record CommandIdMapResponse(IReadOnlyList<CommandIdMapResponseItem> Ids);
internal sealed record CommandIdMapResponseItem(int Slot, string EntityType, string ServerEntityId);
