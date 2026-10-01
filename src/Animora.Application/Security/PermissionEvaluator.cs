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

    /// <summary>Stage 1 shared by single and batched evaluation: active membership + stored role grants, or null.</summary>
    private async Task<(StaffMembership Membership, HashSet<string> Grants)?> LoadGrantsAsync(
        AuthorizationSubject subject, CancellationToken ct)
    {
        var membership = await memberships.FindActiveAsync(subject.TenantId, subject.UserId, ct).ConfigureAwait(false);
        if (membership is null || !membership.IsActive
            || !string.Equals(membership.TenantId, subject.TenantId, StringComparison.Ordinal)
            || !string.Equals(membership.UserId, subject.UserId, StringComparison.Ordinal)
            || membership.Role != subject.Role)
            return null;

        var role = await roles.FindByRoleAsync(subject.TenantId, membership.Role, ct).ConfigureAwait(false);
        if (role is null || role.Role != membership.Role
            || !string.Equals(role.TenantId, subject.TenantId, StringComparison.Ordinal))
            return null;

        var grants = new HashSet<string>(role.PermissionCodes, StringComparer.Ordinal);
        grants.UnionWith(membership.ExtraPermissionCodes);
        grants.ExceptWith(membership.DeniedPermissionCodes);
        return (membership, grants);
    }

    /// <inheritdoc />
    public async Task<IReadOnlySet<string>> AllowedAsync(IReadOnlyCollection<string> permissionCodes,
        AuthorizationSubject subject, CancellationToken ct)
    {
        if (subject is null || string.IsNullOrWhiteSpace(subject.TenantId) || string.IsNullOrWhiteSpace(subject.UserId))
            return new HashSet<string>(StringComparer.Ordinal);
        var stored = await LoadGrantsAsync(subject, ct).ConfigureAwait(false);
        var allowed = new HashSet<string>(StringComparer.Ordinal);
        if (stored is null) return allowed;
        foreach (var code in permissionCodes)
            if (PermissionCodes.Exists(code ?? string.Empty) && stored.Value.Grants.Contains(code!)) allowed.Add(code!);
        return allowed;
    }

    public async Task<AuthorizationDecision> EvaluateAsync(string permissionCode, AuthorizationSubject subject,
        object? resource, CancellationToken ct)
    {
        var code = permissionCode ?? string.Empty;
        if (!PermissionCodes.Exists(code)) return AuthorizationDecision.Deny(code, UnknownRule);
        if (subject is null || string.IsNullOrWhiteSpace(subject.TenantId) || string.IsNullOrWhiteSpace(subject.UserId))
            return AuthorizationDecision.Deny(code, InvalidInputRule);

        // Stage 1 — RBAC from stored grants.
        var stored = await LoadGrantsAsync(subject, ct).ConfigureAwait(false);
        if (stored is null) return AuthorizationDecision.Deny(code, RbacRule);
        var (membership, grants) = stored.Value;
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
