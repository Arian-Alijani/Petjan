using Animora.Persistence.PostgreSql.Tenancy;
using Animora.Server.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
namespace Animora.Server.Hints;

[Authorize(Policy = CoarsePolicies.DeviceActive)]
internal sealed class SyncHintHub(ITenantContext tenant) : Hub
{
    internal static string Group(string tenantId) => "tenant:" + tenantId;
    public override async Task OnConnectedAsync()
    {
        if (tenant.TenantId is not { Length: > 0 } id) { Context.Abort(); return; }
        await Groups.AddToGroupAsync(Context.ConnectionId, Group(id), Context.ConnectionAborted).ConfigureAwait(false);
        await base.OnConnectedAsync().ConfigureAwait(false);
    }
    // No client-invocable Join/Subscribe method. The group is chosen solely by stored tenant membership.
}
