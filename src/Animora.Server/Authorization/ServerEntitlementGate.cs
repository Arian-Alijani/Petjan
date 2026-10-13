using Animora.Domain.Results;

namespace Animora.Server.Authorization;

/// <summary>What the entitlement gate needs to decide: who, in which tenant, doing what.</summary>
/// <param name="TenantId">Tenant resolved from the stored membership.</param>
/// <param name="UserId">User ULID.</param>
/// <param name="CommandType">Wire command type when known (S06+); <see langword="null"/> for plain endpoints.</param>
internal sealed record TenantEntitlementContext(string TenantId, string UserId, string? CommandType);

/// <summary>
/// The server entitlement slot (the <b>last</b> plane: authN → tenant → authZ → entitlement). The real implementation is
/// <c>Licensing.ServerEntitlementGate</c> (registered by <c>AddAnimoraLicensing</c>); there is deliberately NO
/// allow-all fallback (CONS-GATE-01) - an unregistered gate must fail resolution, never silently admit everyone.
/// </summary>
internal interface IServerEntitlementGate
{
    /// <summary><c>Ok</c> when the tenant is entitled; any failure is reported to the client as <c>ENTITLEMENT_REQUIRED</c>.</summary>
    ValueTask<Result> EvaluateAsync(TenantEntitlementContext context, CancellationToken ct = default);
}
