using Animora.Domain.Cashier;
using Animora.Domain.Identity;

namespace Animora.Application.Security;

/// <summary>(Namespace is <c>Animora.Application.Security</c>, not <c>.Rules</c>: a <c>Security.Rules</c> namespace would
/// shadow <c>Validation.Rules</c> inside <c>Animora.Application.Security</c>.) <c>cashier.session.close</c>: only the session's own cashier may close it.</summary>
public sealed class CashSessionAuthorizationRule : IResourceAuthorizationRule<CashSession>
{
    public string PermissionCode => PermissionCodes.CashierSessionClose;

    public bool IsSatisfied(AuthorizationSubject subject, CashSession resource)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(resource);
        return resource.CashierUserId.Length > 0
            && string.Equals(resource.CashierUserId, subject.UserId, StringComparison.Ordinal)
            && string.Equals(resource.TenantId, subject.TenantId, StringComparison.Ordinal);
    }
}
