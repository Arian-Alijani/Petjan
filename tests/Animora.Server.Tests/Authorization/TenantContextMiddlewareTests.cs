using System.Net;
using System.Text.Json;
using Animora.Domain.Identity;
using Animora.Domain.Results;
using Animora.Persistence.PostgreSql.Identity;
using Animora.Server.Authorization;
using Xunit;

namespace Animora.Server.Tests.Authorization;

/// <summary>S05 tenant binding: the tenant comes only from the stored active membership, never from the token.</summary>
public sealed class TenantContextMiddlewareTests
{
    private static async Task<string> CodeOf(HttpResponseMessage response)
    {
        using JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("code").GetString()!;
    }

    [Fact]
    public async Task IgnoresClaimedTenant_UsesMembership()
    {
        var world = new AuthzWorld();
        await using var host = new AuthzHost(world);
        string forged = host.Token(world.Alice, world.AliceDevice, tenantClaim: world.ForeignTenantId);
        using HttpClient client = host.Client(forged);

        using HttpResponseMessage response = await client.PostAsync("/auth/capabilities", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        using JsonDocument payload = JsonDocument.Parse(body.RootElement.GetProperty("payloadJson").GetString()!);
        Assert.Equal(world.TenantId, payload.RootElement.GetProperty("tenantId").GetString());
    }

    [Fact]
    public async Task StaleSecurityStamp_Returns401()
    {
        var world = new AuthzWorld();
        await using var host = new AuthzHost(world);
        string token = host.Token(world.Alice, world.AliceDevice);
        world.Alice.RotateSecurityStamp();
        using HttpClient client = host.Client(token);

        using HttpResponseMessage response = await client.PostAsync("/auth/capabilities", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(ErrorCodes.SessionExpired.Code, await CodeOf(response));
    }

    [Fact]
    public async Task InactiveMembership_Returns403()
    {
        var world = new AuthzWorld();
        await using var host = new AuthzHost(world);
        string token = host.Token(world.Alice, world.AliceDevice);
        world.MembershipOf(world.Alice).Suspend();
        using HttpClient client = host.Client(token);

        using HttpResponseMessage response = await client.PostAsync("/auth/capabilities", content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(ErrorCodes.MembershipNotActive.Code, await CodeOf(response));
    }

    [Fact]
    public async Task TenantSuspended_Returns403()
    {
        var world = new AuthzWorld();
        await using var host = new AuthzHost(world);
        string token = host.Token(world.Alice, world.AliceDevice);
        world.SetTenantStatus(TenantStatus.Suspended);
        using HttpClient client = host.Client(token);

        using HttpResponseMessage response = await client.PostAsync("/auth/capabilities", content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(ErrorCodes.TenantSuspended.Code, await CodeOf(response));
    }

    [Fact]
    public async Task DisabledUser_Returns403()
    {
        var world = new AuthzWorld();
        await using var host = new AuthzHost(world);
        string token = host.Token(world.Bob, world.BobDevice);
        world.Bob.Disable();
        // Disable() rotates the stamp: re-issue under the new stamp to isolate the disabled check.
        string current = host.Token(world.Bob, world.BobDevice);
        using HttpClient client = host.Client(current);

        using HttpResponseMessage stale = await host.Client(token).PostAsync("/auth/capabilities", content: null);
        using HttpResponseMessage response = await client.PostAsync("/auth/capabilities", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, stale.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(ErrorCodes.UserDisabled.Code, await CodeOf(response));
    }

    [Fact]
    public async Task RevokedDevice_Returns403()
    {
        var world = new AuthzWorld();
        await using var host = new AuthzHost(world);
        string token = host.Token(world.Bob, world.BobDevice);
        world.BobDevice.Revoke("lost", world.Clock);
        using HttpClient client = host.Client(token);

        using HttpResponseMessage response = await client.PostAsync("/auth/capabilities", content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(ErrorCodes.DeviceRevoked.Code, await CodeOf(response));
    }

    [Fact]
    public async Task ForeignTenantHint_IsAuditedAndIgnored()
    {
        var world = new AuthzWorld();
        await using var host = new AuthzHost(world);
        string token = host.Token(world.Alice, world.AliceDevice);
        using HttpClient client = host.Client(token, tenantHeader: world.ForeignTenantId);

        using HttpResponseMessage response = await client.PostAsync("/auth/capabilities", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        SecurityAuditEntry hint = Assert.Single(world.Audit.Entries, e => e.Action == TenantContextMiddleware.TenantHintIgnoredAction);
        Assert.Equal(world.TenantId, hint.TenantId);
        Assert.Equal(TenantContextMiddleware.AuditCategory, hint.Category);
        Assert.Equal(world.Alice.Id, hint.ActorUserId);
    }

    [Fact]
    public async Task MatchingTenantHint_IsNotAudited()
    {
        var world = new AuthzWorld();
        await using var host = new AuthzHost(world);
        string token = host.Token(world.Alice, world.AliceDevice);
        using HttpClient client = host.Client(token, tenantHeader: world.TenantId);

        using HttpResponseMessage response = await client.PostAsync("/auth/capabilities", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(world.Audit.Entries, e => e.Action == TenantContextMiddleware.TenantHintIgnoredAction);
    }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    [InlineData("/auth/login")]
    [InlineData("/auth/refresh")]
    [InlineData("/auth/keys")]
    public void AnonymousRoutes_SkipTenantBinding(string path) =>
        Assert.True(TenantContextMiddleware.IsSkipped(new Microsoft.AspNetCore.Http.PathString(path)));

    [Theory]
    [InlineData("/auth/logout")]
    [InlineData("/auth/capabilities")]
    [InlineData("/auth/device/abc/approve")]
    [InlineData("/api/commands")]
    public void ProtectedRoutes_RunTenantBinding(string path) =>
        Assert.False(TenantContextMiddleware.IsSkipped(new Microsoft.AspNetCore.Http.PathString(path)));
}
