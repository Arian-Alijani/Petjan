using Animora.Application.Abstractions;
using Animora.Application.Ports;
using Animora.Application.Security;
using Animora.Application.UseCases.Identity;
using Animora.Application.UseCases.Profile;
using Animora.Application.UseCases.Visits;
using Animora.Client.Services.Profile;
using Animora.Contracts.Profile;
using Animora.Domain.Identity;
using Animora.Domain.Results;
using Animora.Persistence.Sqlite.Options;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Animora.Application.Tests;

public sealed class StaffManagementTests
{
    [Fact]
    public async Task FreshClinic_ContainsOnlyOwner_AndFiveEmptyRoleCounts()
    {
        Assert.False(new LocalStoreOptions().SeedSampleData);
        await using var host = await OwnerHostAsync();
        var directory = await Service(host).GetStaffAsync();
        var owner = Assert.Single(directory.Members);
        Assert.Equal("owner", owner.Username);
        Assert.True(owner.IsProtected);
        Assert.True(directory.CanManageUsers);
        Assert.True(directory.CanManageAccess);
        Assert.Equal(5, directory.Roles.Count);
        Assert.Equal(PermissionCodes.All.Count, directory.Permissions.Count);
        Assert.All(directory.Roles.Where(r => r.Id != "Manager"), role =>
            Assert.DoesNotContain(directory.Members, m => m.RoleId == role.Id));
    }

    [Theory]
    [InlineData("Doctor")]
    [InlineData("Receptionist")]
    [InlineData("Cashier")]
    [InlineData("Accountant")]
    [InlineData("Manager")]
    public async Task CreateRole_PersistsCredentialsAndMembership_ThenCanSignIn(string role)
    {
        await using var host = await OwnerHostAsync();
        await Service(host).AddStaffAsync(new CreateStaffRequest("Staff Person", " StaffLogin ", "Secret456", role, "09120000000"));
        var directory = await Service(host).GetStaffAsync();
        Assert.Equal(2, directory.Members.Count);
        var staff = Assert.Single(directory.Members.Where(m => m.Username == "stafflogin"));
        Assert.Equal(role, staff.RoleId);
        await host.RestartAsync();
        var login = await host.SendAnonymousAsync(new SignInCommand("STAFFLOGIN", "Secret456", false));
        Assert.True(login.IsSuccess, login.Detail);
        Assert.Equal(staff.UserId, login.Value!.UserId);
        Assert.Equal(role, login.Value.Role.ToString());
        using var scope = host.Services.CreateScope();
        var user = await scope.ServiceProvider.GetRequiredService<IIdentityRepository>().FindAsync(staff.UserId);
        Assert.NotNull(user?.PasswordHash);
        Assert.NotEqual("Secret456", user!.PasswordHash);
    }

    [Fact]
    public async Task InvalidCreation_DoesNotAddUsers_AndCannotLeakPasswordThroughToString()
    {
        await using var host = await OwnerHostAsync();
        var service = Service(host);
        var duplicate = await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddStaffAsync(
            new CreateStaffRequest("Other", " OWNER ", "Secret456", "Doctor", "")));
        Assert.Equal("USERNAME_EXISTS", duplicate.Message);
        var weak = await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddStaffAsync(
            new CreateStaffRequest("Other", "other", "weak", "Doctor", "")));
        Assert.Equal(ErrorCodes.PasswordPolicyViolation.Code, weak.Message);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddStaffAsync(
            new CreateStaffRequest("Other", "other", "Secret456", "Assistant", "")));
        Assert.Single((await service.GetStaffAsync()).Members);
        Assert.DoesNotContain("Secret456", new CreateStaffCommand("Other", "other", "Secret456", StaffRole.Doctor, "").ToString());
    }

    [Fact]
    public async Task SavedOverrides_SurviveRestart_AndGateVisitReadWriteCompleteIndependently()
    {
        await using var host = await OwnerHostAsync();
        var service = Service(host);
        await service.AddStaffAsync(new CreateStaffRequest("Doctor", "doctor", "Secret456", "Doctor", ""));
        var member = Assert.Single((await service.GetStaffAsync()).Members.Where(m => m.Username == "doctor"));
        await service.SaveStaffAccessAsync(member.MembershipId, [PermissionCodes.VisitsRead, PermissionCodes.ReportsRead]);
        var saved = Assert.Single((await service.GetStaffAsync()).Members.Where(m => m.UserId == member.UserId));
        Assert.Equal(new[] { PermissionCodes.ReportsRead, PermissionCodes.VisitsRead }, saved.PermissionCodes.Order().ToArray());
        await host.RestartAsync();
        Assert.True((await host.SendAnonymousAsync(new SignInCommand("doctor", "Secret456", false))).IsSuccess);
        using var scope = host.Services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<ICurrentUserProvider>();
        var gate = scope.ServiceProvider.GetRequiredService<IAuthorizationGate>();
        var commandContext = (await provider.BuildCommandContextAsync(null, null, null, default)).Value!;
        Assert.True((await gate.EvaluateAsync(new EncounterDraftQuery("missing"), provider.BuildQueryContext().Value!, default)).IsSuccess);
        Assert.Equal(ErrorCodes.PermissionDenied, (await gate.EvaluateAsync(
            new SaveVisitVitalsCommand("missing", null, null, null, null), commandContext, default)).Error);
        Assert.Equal(ErrorCodes.PermissionDenied, (await gate.EvaluateAsync(
            new CompleteVisitCommand("missing"), commandContext, default)).Error);
        Assert.False((await Service(host).GetStaffAsync()).CanManageUsers);
        Assert.Empty((await Service(host).GetStaffAsync()).Members);
        var denied = await host.SendAsUserAsync(new CreateStaffCommand("Intruder", "intruder", "Secret789", StaffRole.Manager, ""));
        Assert.Equal(ErrorCodes.PermissionDenied, denied.Error);
    }

    [Fact]
    public async Task Disable_IsReversible_PreservesIdentity_AndBlocksOldSession()
    {
        await using var host = await OwnerHostAsync();
        var service = Service(host);
        await service.AddStaffAsync(new CreateStaffRequest("Doctor", "doctor", "Secret456", "Doctor", ""));
        var member = Assert.Single((await service.GetStaffAsync()).Members.Where(m => m.Username == "doctor"));
        Assert.True((await host.SendAnonymousAsync(new SignInCommand("doctor", "Secret456", false))).IsSuccess);
        var oldSession = host.Cache.Current!;
        Assert.True((await host.SendAnonymousAsync(new SignInCommand("owner", "Secret123", false))).IsSuccess);
        await service.SetStaffActiveAsync(member.MembershipId, false);
        Assert.Equal(2, (await service.GetStaffAsync()).Members.Count);
        Assert.False(Assert.Single((await service.GetStaffAsync()).Members.Where(m => m.UserId == member.UserId)).IsActive);
        host.Cache.Set(oldSession);
        var blocked = await host.SendAsUserAsync(SaveUserProfileCommand.For(AppSettingsKeys.ProfilePhone, "0912"));
        Assert.Equal(ErrorCodes.PermissionDenied, blocked.Error);
        Assert.True((await host.SendAnonymousAsync(new SignInCommand("owner", "Secret123", false))).IsSuccess);
        await service.SetStaffActiveAsync(member.MembershipId, true);
        Assert.True((await host.SendAnonymousAsync(new SignInCommand("doctor", "Secret456", false))).IsSuccess);
    }

    [Fact]
    public async Task ProtectedOwner_AndUnknownPermission_AreRejectedWithoutMutation()
    {
        await using var host = await OwnerHostAsync();
        var service = Service(host);
        var owner = Assert.Single((await service.GetStaffAsync()).Members);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SetStaffActiveAsync(owner.MembershipId, false));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveStaffAccessAsync(owner.MembershipId, []));
        await service.AddStaffAsync(new CreateStaffRequest("Doctor", "doctor", "Secret456", "Doctor", ""));
        var doctor = Assert.Single((await service.GetStaffAsync()).Members.Where(m => m.RoleId == "Doctor"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveStaffAccessAsync(doctor.MembershipId, ["visits.unknown"]));
        Assert.Equal(doctor.PermissionCodes, Assert.Single((await service.GetStaffAsync()).Members.Where(m => m.UserId == doctor.UserId)).PermissionCodes);
        Assert.True(Assert.Single((await service.GetStaffAsync()).Members.Where(m => m.UserId == owner.UserId)).IsActive);
    }

    private static UserProfileService Service(LocalIdentityHost host) =>
        new(host.Services.GetRequiredService<IServiceScopeFactory>());

    private static async Task<LocalIdentityHost> OwnerHostAsync()
    {
        var host = new LocalIdentityHost();
        await host.InitializeAsync();
        var setup = await host.SendAnonymousAsync(new CompleteFirstRunSetupCommand("Clinic", "Owner", "owner", "Secret123"));
        Assert.True(setup.IsSuccess, setup.Detail);
        return host;
    }
}
