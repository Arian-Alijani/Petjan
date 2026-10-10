namespace Animora.Domain.Subscriptions;

public enum ActorKind : short { Operator = 0, WebsiteIntent = 1, System = 2 }
public sealed record ActorRef(ActorKind Kind, string? ActorId = null);
