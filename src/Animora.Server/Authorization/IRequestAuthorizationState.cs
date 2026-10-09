using Animora.Application.Security;

namespace Animora.Server.Authorization;

/// <summary>
/// Per-request cache of the single membership resolution done by <see cref="TenantContextMiddleware"/>, so the
/// authorization handlers and <see cref="EntitlementMiddleware"/> reuse it without a second database round-trip.
/// </summary>
internal interface IRequestAuthorizationState
{
    /// <summary>The resolved membership, or <see langword="null"/> before the middleware has run / on skipped routes.</summary>
    ResolvedMembership? Membership { get; }

    /// <summary>The subject built from stored RBAC, or <see langword="null"/> when not resolved.</summary>
    AuthorizationSubject? Subject { get; }

    /// <summary>Stores the resolution for the remainder of the request.</summary>
    void Set(ResolvedMembership membership, AuthorizationSubject subject);
}

/// <inheritdoc cref="IRequestAuthorizationState" />
internal sealed class RequestAuthorizationState : IRequestAuthorizationState
{
    /// <inheritdoc />
    public ResolvedMembership? Membership { get; private set; }

    /// <inheritdoc />
    public AuthorizationSubject? Subject { get; private set; }

    /// <inheritdoc />
    public void Set(ResolvedMembership membership, AuthorizationSubject subject)
    {
        ArgumentNullException.ThrowIfNull(membership);
        ArgumentNullException.ThrowIfNull(subject);
        Membership = membership;
        Subject = subject;
    }
}
