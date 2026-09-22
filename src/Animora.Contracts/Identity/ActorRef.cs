namespace Animora.Contracts.Identity;

/// <summary>The user who performed a recorded action (audit "who").
/// Denormalized on write so history stays correct even if the user is later
/// renamed/removed. The seam a future auth/session layer fills.</summary>
public sealed record ActorRef(string UserId, string Name, string RoleLabel, string Initials)
{
    public static readonly ActorRef System = new("system", "سیستم", "خودکار", "س");
}
