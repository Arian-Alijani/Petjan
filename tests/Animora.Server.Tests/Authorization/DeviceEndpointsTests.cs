using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Animora.Contracts.Wire.Auth;
using Animora.Domain.Identity;
using Animora.Domain.Results;
using Animora.Server.Auth;
using Xunit;

namespace Animora.Server.Tests.Authorization;

public sealed class DeviceEndpointsTests
{
    private static async Task<string> CodeOf(HttpResponseMessage response)
    {
        using JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("code").GetString()!;
    }

    [Fact]
    public async Task Revoke_RotatesSecurityStamp_InvalidatesToken()
    {
        var world = new AuthzWorld();
        await using var host = new AuthzHost(world);
        string bobToken = host.Token(world.Bob, world.BobDevice);
        string stampBefore = world.Bob.SecurityStamp;
        using HttpClient manager = host.Client(host.Token(world.Alice, world.AliceDevice));
        using HttpClient bob = host.Client(bobToken);

        using HttpResponseMessage beforeRevoke = await bob.PostAsync("/auth/capabilities", content: null);
        using HttpResponseMessage revoke = await manager.PostAsJsonAsync($"/auth/device/{world.BobDevice.Id}/revoke", new DeviceRevokeRequest("laptop stolen"));
        using HttpResponseMessage afterRevoke = await bob.PostAsync("/auth/capabilities", content: null);

        Assert.Equal(HttpStatusCode.OK, beforeRevoke.StatusCode);
        Assert.Equal(HttpStatusCode.OK, revoke.StatusCode);
        Assert.Equal(DeviceStatus.Revoked, world.BobDevice.Status);
        Assert.Equal("laptop stolen", world.BobDevice.RevocationReason);
        Assert.NotEqual(stampBefore, world.Bob.SecurityStamp);

        // The live token carries the old stamp: rejected as an expired session before anything else is looked at.
        Assert.Equal(HttpStatusCode.Unauthorized, afterRevoke.StatusCode);
        Assert.Equal(ErrorCodes.SessionExpired.Code, await CodeOf(afterRevoke));
        Assert.Contains(world.Audit.Entries, e => e.Action == DeviceAdminService.DeviceRevokedAction && e.ActorUserId == world.Alice.Id);
    }

    [Fact]
    public async Task Revoke_ReissuedTokenOnRevokedDevice_IsDeviceRevoked()
    {
        var world = new AuthzWorld();
        await using var host = new AuthzHost(world);
        using HttpClient manager = host.Client(host.Token(world.Alice, world.AliceDevice));

        using HttpResponseMessage revoke = await manager.PostAsJsonAsync($"/auth/device/{world.BobDevice.Id}/revoke", new DeviceRevokeRequest("lost"));
        string freshStampToken = host.Token(world.Bob, world.BobDevice); // newest stamp, but the device stays revoked
        using HttpResponseMessage response = await host.Client(freshStampToken).PostAsync("/auth/capabilities", content: null);

        Assert.Equal(HttpStatusCode.OK, revoke.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(ErrorCodes.DeviceRevoked.Code, await CodeOf(response));
    }

    [Fact]
    public async Task Approve_ActivatesPendingDevice_AndIsAudited()
    {
        var world = new AuthzWorld();
        await using var host = new AuthzHost(world);
        Device pending = world.AddDevice(world.Bob, approve: false);
        using HttpClient manager = host.Client(host.Token(world.Alice, world.AliceDevice));

        using HttpResponseMessage response = await manager.PostAsync($"/auth/device/{pending.Id}/approve", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(DeviceStatus.Active, pending.Status);
        DeviceRegisterResponse body = (await response.Content.ReadFromJsonAsync<DeviceRegisterResponse>())!;
        Assert.Equal(pending.Id, body.DeviceId);
        Assert.Contains(world.Audit.Entries, e => e.Action == DeviceAdminService.DeviceApprovedAction);
    }

    [Fact]
    public async Task Approve_RevokedDevice_IsRejected()
    {
        var world = new AuthzWorld();
        await using var host = new AuthzHost(world);
        Device revoked = world.AddDevice(world.Bob, approve: true);
        revoked.Revoke("x", world.Clock);
        using HttpClient manager = host.Client(host.Token(world.Alice, world.AliceDevice));

        using HttpResponseMessage response = await manager.PostAsync($"/auth/device/{revoked.Id}/approve", content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(DeviceStatus.Revoked, revoked.Status);
    }

    [Fact]
    public async Task UnknownDevice_Returns404()
    {
        var world = new AuthzWorld();
        await using var host = new AuthzHost(world);
        using HttpClient manager = host.Client(host.Token(world.Alice, world.AliceDevice));

        using HttpResponseMessage approve = await manager.PostAsync("/auth/device/nope/approve", content: null);
        using HttpResponseMessage revoke = await manager.PostAsJsonAsync("/auth/device/nope/revoke", new DeviceRevokeRequest("x"));

        Assert.Equal(HttpStatusCode.NotFound, approve.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, revoke.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Revoke_WithoutReason_Returns400_AndChangesNothing(string? reason)
    {
        var world = new AuthzWorld();
        await using var host = new AuthzHost(world);
        string stamp = world.Bob.SecurityStamp;
        using HttpClient manager = host.Client(host.Token(world.Alice, world.AliceDevice));

        using HttpResponseMessage response = reason is null
            ? await manager.PostAsync($"/auth/device/{world.BobDevice.Id}/revoke", content: null)
            : await manager.PostAsJsonAsync($"/auth/device/{world.BobDevice.Id}/revoke", new DeviceRevokeRequest(reason));

        Assert.True(response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnsupportedMediaType);
        Assert.Equal(DeviceStatus.Active, world.BobDevice.Status);
        Assert.Equal(stamp, world.Bob.SecurityStamp);
    }

    [Fact]
    public async Task NonManager_CannotApproveOrRevoke()
    {
        var world = new AuthzWorld();
        await using var host = new AuthzHost(world);
        using HttpClient bob = host.Client(host.Token(world.Bob, world.BobDevice));

        using HttpResponseMessage revoke = await bob.PostAsJsonAsync($"/auth/device/{world.AliceDevice.Id}/revoke", new DeviceRevokeRequest("coup"));

        Assert.Equal(HttpStatusCode.Forbidden, revoke.StatusCode);
        Assert.Equal(DeviceStatus.Active, world.AliceDevice.Status);
    }
}
