using Animora.Persistence.PostgreSql.Identity;
using Animora.Persistence.PostgreSql.Tenancy;

namespace Animora.Server.Authorization;

/// <summary>
/// The request-scoped <see cref="ITenantContext"/> (S05). It is bound exactly once per request by the
/// membership resolver from the <b>server-stored</b> active membership — never from a token claim, header,
/// query or body value. It replaces <see cref="AuthTenantContext"/> as the registered
/// <see cref="ITenantContext"/>.
/// </summary>
/// <remarks>
/// The S04 sign-in/refresh/logout flow still binds <see cref="AuthTenantContext"/> directly (it resolves the
/// tenant from a login code / refresh token before any membership exists). To keep a single binding per
/// scope, this context <b>delegates</b> to that scoped instance: whichever plane binds first, the
/// <c>DbContext</c> tenant filter sees the same tenant, and a re-bind to a different tenant is refused.
/// </remarks>
internal sealed class RequestTenantContext(AuthTenantContext binding) : ITenantContext
{
    /// <inheritdoc />
    public string? TenantId => binding.TenantId;

    /// <inheritdoc />
    public bool IsOperator => false;

    /// <summary>Binds the tenant for this request. Re-binding to a different tenant throws.</summary>
    /// <param name="tenantId">The tenant ULID taken from the stored membership.</param>
    public void Bind(string tenantId) => binding.Bind(tenantId);
}
