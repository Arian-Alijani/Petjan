using System.Collections.Frozen;
using Animora.Application.UseCases.Identity;

namespace Animora.Application.Security;

/// <summary>
/// The requests that must run <b>before</b> a tenant identity and licence exist:
/// <see cref="SignInCommand"/>, <see cref="CompleteFirstRunSetupCommand"/>, <see cref="ProvisionLocalMirrorUserCommand"/>,
/// <see cref="UnlockSessionCommand"/> and <see cref="CurrentSessionQuery"/>. They are exempt from both the authorization plane
/// (<see cref="PermissionAuthorizationGate"/>) and the entitlement plane (<c>EntitlementBehavior</c>):
/// gating sign-in itself on a licence is a bootstrap deadlock, because the licence is only sealed
/// locally <i>after</i> the first successful online sign-in (A-RULE 023).
/// </summary>
public static class PreAuthenticationRequests
{
    /// <summary>The pre-authentication request types, resolved once at startup.</summary>
    public static readonly FrozenSet<Type> Types = new[]
    {
        typeof(SignInCommand),
        typeof(CompleteFirstRunSetupCommand),
        typeof(ProvisionLocalMirrorUserCommand),
        typeof(UnlockSessionCommand),
        typeof(CurrentSessionQuery),
    }.ToFrozenSet();

    /// <summary><see langword="true"/> when <paramref name="request"/> is one of the pre-authentication requests.</summary>
    public static bool Contains(object request) =>
        request is not null && Types.Contains(request.GetType());
}
