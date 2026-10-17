using Animora.Server.Auth;
using Animora.Contracts.Wire.Auth;
using Animora.Domain.Identity;
using Animora.Domain.Results;
using Xunit;

namespace Animora.Server.Tests.Identity;

public sealed class LoginServiceTests
{
    [Fact]
    public async Task UnknownTenant_UnknownUser_WrongPassword_ReturnSameError()
    {
        var h = new AuthHarness();

        var unknownTenant = await h.Service.LoginAsync(h.Login(tenantCode: "nope"), "c1");
        var unknownUser = await h.Service.LoginAsync(h.Login(userName: "ghost"), "c2");
        var wrongPassword = await h.Service.LoginAsync(h.Login(password: "Wrong-pass-1"), "c3");

        foreach (var result in new[] { unknownTenant, unknownUser, wrongPassword })
        {
            Assert.True(result.IsFailure);
            Assert.Equal(ErrorCodes.CredentialsInvalid.Code, result.Error.Code);
            Assert.Null(result.Detail);
        }

        Assert.Equal(unknownTenant.Error, unknownUser.Error);
        Assert.Equal(unknownUser.Error, wrongPassword.Error);
    }

    [Fact]
    public async Task Login_Success_IssuesTokens_BindsDevice_AndAudits()
    {
        var h = new AuthHarness();

        var result = await h.Service.LoginAsync(h.Login(), "corr-1");

        Assert.True(result.IsSuccess);
        LoginResponse body = result.Value!;
        Assert.Equal(h.User.Id, body.UserId);
        Assert.Equal(h.TenantId, body.TenantId);
        Assert.Equal(nameof(StaffRole.Manager), body.Role);
        Assert.Equal(900, body.ExpiresInSeconds);
        Assert.StartsWith(h.TenantId + ".", body.RefreshToken, StringComparison.Ordinal);
        Assert.Single(h.Repository.Devices);
        Assert.Equal(h.Repository.Devices[0].Id, body.DeviceId);
        Assert.Contains(h.Audit.Entries, e => e.Action == "auth.login.success" && e.CorrelationId == "corr-1");
        Assert.NotNull(h.User.LastLoginUtc);
    }

    [Fact]
    public async Task ConcurrentFirstLogin_ConvergesOnOneDevice_NoDuplicateRows()
    {
        var h = new AuthHarness();

        // A concurrent first login commits the same (user, installation) device just before ours inserts.
        Device? winner = null;
        h.Repository.OnBeforeAddDevice = _ =>
        {
            winner = Device.Register(h.TenantId, h.User.Id, "Unnamed device", AuthHarness.Installation, "unknown", h.Clock);
            h.Repository.Devices.Add(winner);
            h.Repository.OnBeforeAddDevice = null;
        };

        var result = await h.Service.LoginAsync(h.Login(), "race");

        Assert.True(result.IsSuccess);
        Assert.Single(h.Repository.Devices);
        Assert.Equal(winner!.Id, result.Value!.DeviceId);
        // The loser adopted an existing row, so it is not audited as a fresh registration.
        Assert.DoesNotContain(h.Audit.Entries, e => e.DeviceId == winner.Id && e.DetailJson != null && e.DetailJson.Contains("device_registered"));
    }

    [Fact]
    public async Task RevokedDevice_Returns_DEVICE_REVOKED()
    {
        var h = new AuthHarness();
        var device = Device.Register(h.TenantId, h.User.Id, "Front desk", AuthHarness.Installation, "windows", h.Clock);
        device.Revoke("lost", h.Clock);
        h.Repository.Devices.Add(device);

        var result = await h.Service.LoginAsync(h.Login(), "c");

        Assert.True(result.IsFailure);
        Assert.Equal("DEVICE_REVOKED", result.Error.Code);
        Assert.Contains(h.Audit.Entries, e => e.Action == "auth.device.revoked_contact" && e.DeviceId == device.Id);
        Assert.Empty(h.TokenStore.Rows);
    }

    [Fact]
    public async Task FiveBadPasswords_Lock_Then_Unlock_After15Minutes()
    {
        var h = new AuthHarness();

        for (int i = 0; i < User.MaxFailedAttempts - 1; i++)
        {
            Assert.Equal("CREDENTIALS_INVALID", (await h.Service.LoginAsync(h.Login(password: "Bad-pass-1"), "c")).Error.Code);
        }

        Assert.Equal("USER_LOCKED_OUT", (await h.Service.LoginAsync(h.Login(password: "Bad-pass-1"), "c")).Error.Code);
        Assert.Contains(h.Audit.Entries, e => e.Action == "auth.login.locked");

        // Even the right password is refused while locked.
        Assert.Equal("USER_LOCKED_OUT", (await h.Service.LoginAsync(h.Login(), "c")).Error.Code);

        h.Clock.Advance(TimeSpan.FromMinutes(16));
        Assert.True((await h.Service.LoginAsync(h.Login(), "c")).IsSuccess);
    }

    [Fact]
    public async Task DisabledUser_RevealedOnlyWithCorrectPassword()
    {
        var h = new AuthHarness();
        h.User.Disable();

        Assert.Equal("CREDENTIALS_INVALID", (await h.Service.LoginAsync(h.Login(password: "Wrong-pass-1"), "c")).Error.Code);
        Assert.Equal("USER_DISABLED", (await h.Service.LoginAsync(h.Login(), "c")).Error.Code);
    }

    [Fact]
    public async Task SuspendedTenant_Returns_TENANT_SUSPENDED()
    {
        var h = new AuthHarness();
        h.Tenants.Tenants.Clear();
        h.Tenants.Tenants.Add(new(h.TenantId, AuthHarness.TenantCode, "Clinic", TenantStatus.Suspended));

        Assert.Equal("TENANT_SUSPENDED", (await h.Service.LoginAsync(h.Login(), "c")).Error.Code);
    }

    [Fact]
    public async Task InvalidRequest_Returns_ValidationFailed()
    {
        var h = new AuthHarness();

        Assert.Equal(ErrorCodes.ValidationFailed.Code, (await h.Service.LoginAsync(null, "c")).Error.Code);
        Assert.Equal(ErrorCodes.ValidationFailed.Code, (await h.Service.LoginAsync(new LoginRequest("", "", "", ""), "c")).Error.Code);
    }

    [Fact]
    public async Task Logout_RevokesDeviceTokens_AndAudits()
    {
        var h = new AuthHarness();
        var login = (await h.Service.LoginAsync(h.Login(), "c")).Value!;

        var caller = new AuthenticatedCaller(login.UserId, login.TenantId, login.DeviceId, h.User.SecurityStamp);
        var result = await h.NewRequestScope().LogoutAsync(caller, "c-out");

        Assert.True(result.IsSuccess);
        Assert.All(h.TokenStore.Rows, r => Assert.NotNull(r.RevokedUtc));
        Assert.Contains(h.Audit.Entries, e => e.Action == "auth.logout");
    }

    [Fact]
    public async Task Logout_WithStaleSecurityStamp_IsRejected()
    {
        var h = new AuthHarness();
        var login = (await h.Service.LoginAsync(h.Login(), "c")).Value!;
        var stale = new AuthenticatedCaller(login.UserId, login.TenantId, login.DeviceId, "old-stamp");

        Assert.Equal("SESSION_EXPIRED", (await h.NewRequestScope().LogoutAsync(stale, "c")).Error.Code);
    }

    [Fact]
    public async Task RegisterDevice_CreatesActive_AndIsIdempotent()
    {
        var h = new AuthHarness();
        var login = (await h.Service.LoginAsync(h.Login(), "c")).Value!;
        var caller = new AuthenticatedCaller(login.UserId, login.TenantId, login.DeviceId, h.User.SecurityStamp);
        var request = new DeviceRegisterRequest("Reception PC", "install-hash-2", "windows");

        var first = await h.NewRequestScope().RegisterDeviceAsync(caller, request, "c");
        var second = await h.NewRequestScope().RegisterDeviceAsync(caller, request, "c");

        Assert.True(first.IsSuccess);
        Assert.Equal(nameof(DeviceStatus.Active), first.Value!.Status);
        Assert.Equal(first.Value.DeviceId, second.Value!.DeviceId);
        Assert.Contains(h.Audit.Entries, e => e.Action == "auth.device.registered");
    }
}
