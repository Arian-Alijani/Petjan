using System.Collections.Frozen;
using Animora.Application.Ports;
using Animora.Domain.Cashier;
using Animora.Domain.Identity;
using Animora.Domain.Sales;

namespace Animora.Application.Security;

/// <summary>
/// Ordered, fail-fast evaluation (first denial wins):
/// <list type="number">
/// <item>catalog → <c>PERMISSION_UNKNOWN</c>; inputs → <c>INVALID_INPUT</c>;</item>
/// <item>RBAC from the STORE: active membership + tenant role definition; grants = role ∪ extra − denied (ordinal).
///       A missing role definition grants nothing (no <c>DefaultRolePermissions</c> fallback) → <c>RBAC</c>;</item>
/// <item>named policies bound to <c>(code, resource type)</c> → the policy name;</item>
/// <item>registered instance rules → the rule's type name.</item>
/// </list>
/// Caller-supplied <see cref="AuthorizationSubject.Permissions"/> and <see cref="AuthorizationSubject.Role"/> are
/// never trusted; later stages see a subject rebuilt from stored facts.
/// </summary>
public sealed class PermissionEvaluator(
    IStaffMembershipRepository memberships,
    IRoleDefinitionRepository roles,
    IResourceAuthorizationRule<Invoice> invoiceRule,
    IResourceAuthorizationRule<CashSession> cashSessionRule) : IPermissionEvaluator
{
    public const string UnknownRule = "PERMISSION_UNKNOWN";
    public const string InvalidInputRule = "INVALID_INPUT";
    public const string RbacRule = "RBAC";

    public async Task<AuthorizationDecision> EvaluateAsync(string permissionCode, AuthorizationSubject subject,
        object? resource, CancellationToken ct)
    {
        var code = permissionCode ?? string.Empty;
        if (!PermissionCodes.Exists(code)) return AuthorizationDecision.Deny(code, UnknownRule);
        if (subject is null || string.IsNullOrWhiteSpace(subject.TenantId) || string.IsNullOrWhiteSpace(subject.UserId))
            return AuthorizationDecision.Deny(code, InvalidInputRule);

        // Stage 1 — RBAC from stored grants.
        var membership = await memberships.FindActiveAsync(subject.TenantId, subject.UserId, ct).ConfigureAwait(false);
        if (membership is null || !membership.IsActive
            || !string.Equals(membership.TenantId, subject.TenantId, StringComparison.Ordinal)
            || !string.Equals(membership.UserId, subject.UserId, StringComparison.Ordinal)
            || membership.Role != subject.Role)
            return AuthorizationDecision.Deny(code, RbacRule);

        var role = await roles.FindByRoleAsync(subject.TenantId, membership.Role, ct).ConfigureAwait(false);
        if (role is null || role.Role != membership.Role
            || !string.Equals(role.TenantId, subject.TenantId, StringComparison.Ordinal))
            return AuthorizationDecision.Deny(code, RbacRule);

        var grants = new HashSet<string>(role.PermissionCodes, StringComparer.Ordinal);
        grants.UnionWith(membership.ExtraPermissionCodes);
        grants.ExceptWith(membership.DeniedPermissionCodes);
        if (!grants.Contains(code)) return AuthorizationDecision.Deny(code, RbacRule);

        var effective = subject with { Role = membership.Role, Permissions = grants.ToFrozenSet(StringComparer.Ordinal) };
        if (resource is null) return AuthorizationDecision.Allow(code);

        // Stage 2 — named policies for this (code, concrete resource type) pair only.
        foreach (var name in AuthorizationPolicies.PoliciesFor(code, resource))
        {
            if (!AuthorizationPolicies.Predicates.TryGetValue(name, out var predicate) || !predicate(effective, resource))
                return AuthorizationDecision.Deny(code, name);
        }

        // Stage 3 — registered instance rules, dispatched by concrete type (no reflection).
        switch (resource)
        {
            case Invoice invoice when string.Equals(invoiceRule.PermissionCode, code, StringComparison.Ordinal):
                if (!invoiceRule.IsSatisfied(effective, invoice))
                    return AuthorizationDecision.Deny(code, nameof(InvoiceAuthorizationRule));
                break;
            case CashSession session when string.Equals(cashSessionRule.PermissionCode, code, StringComparison.Ordinal):
                if (!cashSessionRule.IsSatisfied(effective, session))
                    return AuthorizationDecision.Deny(code, nameof(CashSessionAuthorizationRule));
                break;
        }

        return AuthorizationDecision.Allow(code);
    }
}
