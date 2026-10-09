using System.Net;
using System.Text.Json;
using Animora.Domain.Results;
using Xunit;

namespace Animora.Server.Tests.Authorization;

/// <summary>The single ordering invariant: authN (401) → tenant → permission authZ (403) → entitlement (403, last).</summary>
public sealed class PlaneOrderTests
{
    private static async Task<string> CodeOf(HttpResponseMessage response)
    {
        using JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("code").GetString()!;
    }

    [Fact]
    public async Task Unauthenticated401_Then_Permission403_Then_Entitlement()
    {
        var world = new AuthzWorld();
        await using var host = new AuthzHost(world, denyEntitlement: true);
        Device_ pending = new(world);

        // 1. No credentials: 401 even though both the permission and the entitlement planes would deny.
        using HttpResponseMessage anonymous = await host.Client().PostAsync($"/auth/device/{pending.Id}/approve", content: null);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(ErrorCodes.SessionExpired.Code, await CodeOf(anonymous));

        // 2. Authenticated but lacking devices.manage, with a deny-all entitlement gate: the PERMISSION denial is reported first.
        using HttpClient receptionist = host.Client(host.Token(world.Bob, world.BobDevice));
        using HttpResponseMessage permission = await receptionist.PostAsync($"/auth/device/{pending.Id}/approve", content: null);
        Assert.Equal(HttpStatusCode.Forbidden, permission.StatusCode);
        Assert.Equal(ErrorCodes.PermissionDenied.Code, await CodeOf(permission));

        // 3. Permission granted: only now does the entitlement plane speak.
        using HttpClient manager = host.Client(host.Token(world.Alice, world.AliceDevice));
        using HttpResponseMessage entitlement = await manager.PostAsync($"/auth/device/{pending.Id}/approve", content: null);
        Assert.Equal(HttpStatusCode.Forbidden, entitlement.StatusCode);
        Assert.Equal(ErrorCodes.EntitlementRequired.Code, await CodeOf(entitlement));
    }

    [Fact]
    public async Task AllowAllGate_NeverBlocks_AndAnonymousRoutesBypassEntitlement()
    {
        var world = new AuthzWorld();
        await using var host = new AuthzHost(world, denyEntitlement: true);

        // Anonymous public routes are never entitlement-gated, even with a deny-all gate.
        using HttpResponseMessage keys = await host.Client().GetAsync("/auth/keys");
        using HttpResponseMessage live = await host.Client().GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, keys.StatusCode);
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);

        await using var open = new AuthzHost(new AuthzWorld());
        AuthzWorld w = open.World;
        using HttpResponseMessage caps = await open.Client(open.Token(w.Alice, w.AliceDevice)).PostAsync("/auth/capabilities", content: null);
        Assert.Equal(HttpStatusCode.OK, caps.StatusCode);
    }

    [Fact]
    public async Task TenantBindingFailure_PrecedesPermissionAndEntitlement()
    {
        var world = new AuthzWorld();
        await using var host = new AuthzHost(world, denyEntitlement: true);
        string token = host.Token(world.Alice, world.AliceDevice);
        world.SetTenantStatus(Animora.Domain.Identity.TenantStatus.Suspended);

        using HttpResponseMessage response = await host.Client(token).PostAsync("/auth/device/x/approve", content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(ErrorCodes.TenantSuspended.Code, await CodeOf(response));
    }

    private sealed class Device_
    {
        public Device_(AuthzWorld world) => Id = world.AddDevice(world.Bob, approve: false).Id;

        public string Id { get; }
    }
}
