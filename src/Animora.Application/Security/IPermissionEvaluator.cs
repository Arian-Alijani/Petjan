using Animora.Domain.Identity;

namespace Animora.Application.Security;

/// <summary>
/// The actor an authorization decision is made for. <see cref="Permissions"/> is an effective-grants
/// snapshot for display/diagnostics only — the evaluator always recomputes grants from the stored
/// membership and tenant role definition and never treats this set as authority.
/// </summary>
public sealed record AuthorizationSubject(
    string TenantId,
    string UserId,
    IReadOnlySet<string> Permissions,
    StaffRole Role,
    string DeviceId,
    bool IsOffline);

/// <summary>
/// Outcome of one evaluation. <see cref="DeniedByRule"/> names the first failing stage:
/// <c>PERMISSION_UNKNOWN</c>, <c>INVALID_INPUT</c>, <c>RBAC</c>, a policy name from
/// <see cref="AuthorizationPolicies"/>, or a resource rule name. <c>null</c> when allowed.
/// </summary>
public sealed record AuthorizationDecision(bool Allowed, string? DeniedByRule, string PermissionCode)
{
    public static AuthorizationDecision Allow(string permissionCode) => new(true, null, permissionCode);

    public static AuthorizationDecision Deny(string permissionCode, string rule) => new(false, rule, permissionCode);
}

/// <summary>
/// Authorization plane evaluator (who may do what). Distinct from authentication (session) and from
/// entitlement (what the tenant has licensed) — A-RULE 023.
/// </summary>
public interface IPermissionEvaluator
{
    Task<AuthorizationDecision> EvaluateAsync(string permissionCode, AuthorizationSubject subject, object? resource,
        CancellationToken ct);
}
