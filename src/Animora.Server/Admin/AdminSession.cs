using Animora.Domain.Subscriptions;
using Animora.Web.Admin.Ports;
using System.Security.Claims;
namespace Animora.Server.Admin;

internal sealed class AdminSession(IHttpContextAccessor accessor) : IAdminSession
{
    private ClaimsPrincipal Principal=>accessor.HttpContext?.User is { } p&&p.Identities.Any(i=>i.IsAuthenticated&&i.AuthenticationType==OperatorCookie.Scheme)
        ?p:throw new UnauthorizedAccessException("A cookie-authenticated operator is required.");
    public string OperatorId=>Principal.FindFirstValue("sub")??throw new UnauthorizedAccessException();
    public string Name=>Principal.Identity?.Name??"Operator";
    public ActorRef Actor=>new(ActorKind.Operator,OperatorId);
}
