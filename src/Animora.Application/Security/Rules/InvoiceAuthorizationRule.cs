using Animora.Domain.Identity;
using Animora.Domain.Sales;

namespace Animora.Application.Security;

/// <summary>
/// <c>sales.draft</c> on an existing invoice means editing it: only a <see cref="InvoiceState.Draft"/>
/// invoice may be mutated. A posted invoice is immutable (A-RULE 002); void/refund use their own
/// Sales codes and never pass through this rule.
/// </summary>
public sealed class InvoiceAuthorizationRule : IResourceAuthorizationRule<Invoice>
{
    public string PermissionCode => PermissionCodes.SalesDraft;

    public bool IsSatisfied(AuthorizationSubject subject, Invoice resource)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(resource);
        return resource.State == InvoiceState.Draft
            && string.Equals(resource.TenantId, subject.TenantId, StringComparison.Ordinal);
    }
}
