using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Animora.Application.Security;
using Animora.Contracts.Wire.Commands;
using Animora.Domain.Identity;
using Animora.Domain.Results;
using Animora.Server.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Xunit;

namespace Animora.Server.Tests.Authorization;

/// <summary>Stored-RBAC evaluation: role ∪ extra − denied, deny wins, unknown permissions fail closed.</summary>
public sealed class AuthorizationTests
{
    private static async Task<string> CodeOf(HttpResponseMessage response)
    {
        using JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("code").GetString()!;
    }

    private static PermissionEvaluator EvaluatorOver(AuthzWorld world) =>
        new(world.Rbac, world.Rbac, new InvoiceAuthorizationRule(), new CashSessionAuthorizationRule());

    private static AuthorizationSubject SubjectOf(AuthzWorld world, User user, StaffRole role) =>
        new(world.TenantId, user.Id, new HashSet<string>(StringComparer.Ordinal), role, "device", IsOffline: false);

    [Fact]
    public void ComputeGrants_DenyWinsOverRoleAndExtra()
    {
        IReadOnlySet<string> grants = ServerAuthorizationSubjectFactory.ComputeGrants(
            role: [PermissionCodes.PatientsRead, PermissionCodes.DevicesManage],
            extra: [PermissionCodes.SalesPost, PermissionCodes.DevicesManage],
            denied: [PermissionCodes.DevicesManage, PermissionCodes.PatientsRead]);

        Assert.Equal([PermissionCodes.SalesPost], grants);
    }

    [Fact]
    public async Task DenyWinsOverRoleAndExtra()
    {
        var world = new AuthzWorld();
        StaffMembership bob = world.MembershipOf(world.Bob);
        bob.GrantExtra(PermissionCodes.DevicesManage);
        bob.Deny(PermissionCodes.DevicesManage);
        StaffMembership alice = world.MembershipOf(world.Alice);
        alice.Deny(PermissionCodes.DevicesManage); // Manager's role grants it; the personal deny still wins.
        PermissionEvaluator evaluator = EvaluatorOver(world);

        AuthorizationDecision forBob = await evaluator.EvaluateAsync(
            PermissionCodes.DevicesManage, SubjectOf(world, world.Bob, StaffRole.Receptionist), null, CancellationToken.None);
        AuthorizationDecision forAlice = await evaluator.EvaluateAsync(
            PermissionCodes.DevicesManage, SubjectOf(world, world.Alice, StaffRole.Manager), null, CancellationToken.None);

        Assert.False(forBob.Allowed);
        Assert.False(forAlice.Allowed);
        Assert.Equal(PermissionEvaluator.RbacRule, forAlice.DeniedByRule);
    }

    [Fact]
    public async Task ExtraGrant_AllowsBeyondRole_OverHttp()
    {
        var world = new AuthzWorld();
        await using var host = new AuthzHost(world);
        string token = host.Token(world.Bob, world.BobDevice);
        using HttpClient client = host.Client(token);
        Device pending = world.AddDevice(world.Bob, approve: false);

        using HttpResponseMessage denied = await client.PostAsync($"/auth/device/{pending.Id}/approve", content: null);
        world.MembershipOf(world.Bob).GrantExtra(PermissionCodes.DevicesManage);
        using HttpResponseMessage allowed = await client.PostAsync($"/auth/device/{pending.Id}/approve", content: null);

        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal(ErrorCodes.PermissionDenied.Code, await CodeOf(denied));
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        Assert.Equal(DeviceStatus.Active, pending.Status);
    }

    [Fact]
    public async Task RoleClaimInToken_IsNeverTrusted()
    {
        var world = new AuthzWorld();
        await using var host = new AuthzHost(world);

        // Bob is a Receptionist in the store; the forged token claims Manager.
        string forged = host.Token(world.Bob, world.BobDevice, roleClaim: StaffRole.Manager.ToString());
        using HttpClient client = host.Client(forged);
        Device pending = world.AddDevice(world.Bob, approve: false);

        using HttpResponseMessage response = await client.PostAsync($"/auth/device/{pending.Id}/approve", content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(DeviceStatus.Pending, pending.Status);
    }

    [Fact]
    public async Task UnknownPermission_FailsClosed()
    {
        var world = new AuthzWorld();
        PermissionEvaluator evaluator = EvaluatorOver(world);
        AuthorizationSubject manager = SubjectOf(world, world.Alice, StaffRole.Manager);

        AuthorizationDecision decision = await evaluator.EvaluateAsync("not.a.permission", manager, null, CancellationToken.None);

        Assert.False(decision.Allowed);
        Assert.Equal(PermissionEvaluator.UnknownRule, decision.DeniedByRule);
    }

    [Fact]
    public async Task PolicyProvider_UnknownName_YieldsNoPolicy_ButCodesAndCoarseNamesResolve()
    {
        var provider = new ServerPermissionPolicyProvider(Options.Create(new AuthorizationOptions()));

        Assert.Null(await provider.GetPolicyAsync("definitely-not-a-policy"));

        AuthorizationPolicy? byCode = await provider.GetPolicyAsync(PermissionCodes.DevicesManage);
        Assert.NotNull(byCode);
        PermissionRequirement requirement = Assert.Single(byCode!.Requirements.OfType<PermissionRequirement>());
        Assert.Equal(PermissionCodes.DevicesManage, requirement.PermissionCode);
    }

    [Fact]
    public async Task CommandAuthorizer_UnmappedCommand_IsDenied_FailClosed()
    {
        var world = new AuthzWorld();
        var authorizer = new CommandAuthorizer(EvaluatorOver(world));

        // S07–S10 seeded real rows (e.g. sales.invoice.post), so fail-closed is asserted on a type no row maps.
        Result result = await authorizer.AuthorizeAsync(
            "unmapped.command.type", SubjectOf(world, world.Alice, StaffRole.Manager), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCodes.PermissionUnknown.Code, result.Error.Code);
    }

    [Fact]
    public void CommandPermissionTable_MapsOnlyKnownPermissions_AndUnmappedLookupIsNull()
    {
        // The table is seeded by the handler phases (S07–S10); every row must name a real permission code.
        Assert.NotEmpty(CommandPermissionTable.Rows);
        Assert.All(CommandPermissionTable.Rows, row => Assert.Contains(row.Value, PermissionCodes.All));
        Assert.Equal(PermissionCodes.SalesPost, CommandPermissionTable.RequiredFor(CommandTypes.SalesInvoicePost));
        Assert.Null(CommandPermissionTable.RequiredFor("anything"));
    }

    [Fact]
    public async Task ManagerOnly_And_OperatorOnly_PoliciesUseStoredRole()
    {
        var world = new AuthzWorld();
        await using var host = new AuthzHost(world);
        using HttpClient client = host.Client(host.Token(world.Alice, world.AliceDevice));

        // Operator identity does not exist before S14: even a Manager is denied (fail closed).
        using HttpResponseMessage operatorProbe = await client.PostAsJsonAsync(
            "/auth/device/none/revoke", new Animora.Contracts.Wire.Auth.DeviceRevokeRequest("x"));

        Assert.NotEqual(HttpStatusCode.Unauthorized, operatorProbe.StatusCode);
        Assert.Equal(["TenantMember", "ManagerOnly", "OperatorOnly", "DeviceActive"], CoarsePolicies.All);
    }
}
