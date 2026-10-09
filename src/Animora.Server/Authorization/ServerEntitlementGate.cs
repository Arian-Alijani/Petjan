using Animora.Domain.Results;

namespace Animora.Server.Authorization;

/// <summary>What the entitlement gate needs to decide: who, in which tenant, doing what.</summary>
/// <param name="TenantId">Tenant resolved from the stored membership.</param>
/// <param name="UserId">User ULID.</param>
/// <param name="CommandType">Wire command type when known (S06+); <see langword="null"/> for plain endpoints.</param>
internal sealed record TenantEntitlementContext(string TenantId, string UserId, string? CommandType);

/// <summary>
/// The server entitlement slot (the <b>last</b> plane: authN → tenant → authZ → entitlement). S05 ships only the
/// slot; S16 replaces the registration with the subscription/license-backed gate.
/// </summary>
internal interface IServerEntitlementGate
{
    /// <summary><c>Ok</c> when the tenant is entitled; any failure is reported to the client as <c>ENTITLEMENT_REQUIRED</c>.</summary>
    ValueTask<Result> EvaluateAsync(TenantEntitlementContext context, CancellationToken ct = default);
}

/// <summary>Allows everything. Registered with <c>TryAdd</c> so S16 can replace it. Never denies until then.</summary>
internal sealed class AllowAllServerEntitlementGate : IServerEntitlementGate
{
    /// <inheritdoc />
    public ValueTask<Result> EvaluateAsync(TenantEntitlementContext context, CancellationToken ct = default) =>
        ValueTask.FromResult(Result.Ok());
}
