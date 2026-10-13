using System.Net;
using Animora.Application.Security;
using Animora.Client.Services.Identity;
using Animora.Domain.Results;
using Animora.Infrastructure.Local.Identity;
using Animora.Server.Licensing.Verification;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Animora.Sync.Tests;

/// <summary>G06: online login / offline credential mirror / device revocation, over a real migrated SQLite file and a wire-faithful fake server.</summary>
public sealed class OnlineAuthClientTests
{
    private static Task<Result<OnlineLoginOutcome>> LoginAsync(OnlineStack s, string password = FakeIdentityServer.Password) =>
        s.Client.LoginAsync(FakeIdentityServer.TenantCode, FakeIdentityServer.UserName, password);

    [Fact]
    public async Task Login200_PersistsSession_AndSealsEntitlement()
    {
        using var stack = await OnlineStack.CreateAsync();

        var login = await LoginAsync(stack);

        Assert.True(login.IsSuccess, login.Detail);
        Assert.False(login.Value!.IsOffline);
        Assert.Equal(stack.Server.UserId, login.Value.UserId);

        var session = (await stack.Services.GetRequiredService<ISessionStore>().ReadAsync()).Value;
        Assert.NotNull(session);
        Assert.Equal(stack.Server.LastAccessToken, session!.AccessToken);
        Assert.Equal("STAMP-1", session.SecurityStamp);

        var sealedValue = (await stack.Services.GetRequiredService<ISealedEntitlementStore>().ReadAsync()).Value;
        Assert.NotNull(sealedValue);
        Assert.Equal(stack.Server.TenantId, sealedValue!.TenantId);
        Assert.True(LicenseEnvelope.Parse(sealedValue.LicenseToken).IsSuccess);

        // The device was registered, the capability snapshot mirrored, and the licence activated — in that online order.
        var paths = stack.Server.Handler.Requests.Select(r => r.Path).ToList();
        Assert.Contains("auth/device/register", paths);
        Assert.Contains("auth/capabilities", paths);
        Assert.Contains("licensing/activate", paths);
        Assert.True((await stack.Services.GetRequiredService<IOfflineCredentialMirrorStore>().ReadAsync()).Value is not null);

        // Access/refresh tokens never sit in SQLite as plaintext.
        using var db = stack.Fixture.CreateContext(stack.Fixture.TenantId);
        var row = db.Set<Animora.Persistence.Sqlite.Entities.ClientSessionRow>().Single();
        Assert.DoesNotContain(stack.Server.LastAccessToken, row.AccessToken, StringComparison.Ordinal);
        Assert.NotNull(row.OfflineCredentialVerifier);
    }

    [Fact]
    public async Task SecondLogin_RenewsInsteadOfActivating()
    {
        using var stack = await OnlineStack.CreateAsync();
        Assert.True((await LoginAsync(stack)).IsSuccess);
        Assert.True((await LoginAsync(stack)).IsSuccess);

        Assert.Equal(1, stack.Server.Handler.Count("licensing/activate"));
        Assert.Equal(1, stack.Server.Handler.Count("licensing/renew"));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "CREDENTIALS_INVALID")]
    [InlineData((HttpStatusCode)423, "USER_LOCKED_OUT")]
    [InlineData(HttpStatusCode.Forbidden, "DEVICE_REVOKED")]
    public async Task DefinitiveServerRefusal_IsFinal_NoOfflineFallback(HttpStatusCode status, string code)
    {
        using var stack = await OnlineStack.CreateAsync();
        Assert.True((await LoginAsync(stack)).IsSuccess); // a mirror exists, so a fallback WOULD be possible
        stack.Server.LoginFailure = (status, code);

        var login = await LoginAsync(stack);

        Assert.True(login.IsFailure);
        Assert.Equal(code, login.Error.Code);
    }

    [Fact]
    public async Task EmptyInput_FailsValidation_WithoutAnyRequest()
    {
        using var stack = await OnlineStack.CreateAsync();

        var login = await stack.Client.LoginAsync("", FakeIdentityServer.UserName, FakeIdentityServer.Password);

        Assert.Equal(ErrorCodes.ValidationFailed, login.Error);
        Assert.Empty(stack.Server.Handler.Requests);
    }

    [Fact]
    public async Task ServerUnreachable_UsesCredentialMirror_WhenWindowsValid()
    {
        using var stack = await OnlineStack.CreateAsync();
        Assert.True((await LoginAsync(stack)).IsSuccess);
        stack.Server.Handler.Unreachable = true;
        stack.Fixture.Clock.Now = stack.Server.Now.AddDays(2); // inside both the capability window (7d) and the licence

        var offline = await LoginAsync(stack);

        Assert.True(offline.IsSuccess, offline.Detail);
        Assert.True(offline.Value!.IsOffline);
        Assert.Equal(stack.Server.UserId, offline.Value.UserId);
    }

    [Fact]
    public async Task ServerUnreachable_WrongPassword_FailsClosed_CredentialMismatch()
    {
        using var stack = await OnlineStack.CreateAsync();
        Assert.True((await LoginAsync(stack)).IsSuccess);
        stack.Server.Handler.Unreachable = true;

        var offline = await LoginAsync(stack, "not-the-password");

        Assert.Equal(ErrorCodes.OfflineCredentialMismatch, offline.Error);
    }

    [Fact]
    public async Task ServerUnreachable_NeverLoggedInBefore_IsNetworkUnavailable()
    {
        using var stack = await OnlineStack.CreateAsync();
        stack.Server.Handler.Unreachable = true;

        var offline = await LoginAsync(stack);

        Assert.Equal(ErrorCodes.NetworkUnavailable, offline.Error);
    }

    [Fact]
    public async Task ServerUnreachable_CapabilityWindowElapsed_FailsClosed_SessionUnavailable()
    {
        using var stack = await OnlineStack.CreateAsync(capabilityWindow: TimeSpan.FromHours(12));
        Assert.True((await LoginAsync(stack)).IsSuccess);
        stack.Server.Handler.Unreachable = true;
        stack.Fixture.Clock.Now = stack.Server.Now.AddDays(3); // capability snapshot (12h) is long gone, licence still fine

        var offline = await LoginAsync(stack);

        Assert.Equal(ErrorCodes.OfflineSessionUnavailable, offline.Error);
        Assert.Equal("capability_expired", offline.Detail);
    }

    [Fact]
    public async Task ServerUnreachable_BackwardClock_CannotExtendOfflineWindow()
    {
        using var stack = await OnlineStack.CreateAsync(capabilityWindow: TimeSpan.FromDays(1));
        Assert.True((await LoginAsync(stack)).IsSuccess);
        stack.Server.Handler.Unreachable = true;

        // The device has already observed a time past the capability window; the OS clock is then wound back to the login instant.
        stack.Monotonic.Observe(stack.Server.Now.AddDays(5));
        stack.Fixture.Clock.Now = stack.Server.Now;

        var offline = await LoginAsync(stack);

        Assert.Equal(ErrorCodes.OfflineSessionUnavailable, offline.Error);
    }

    [Fact]
    public async Task DeviceRevoked_OnRegister_AbortsLogin_AndWipesOfflineAuthority()
    {
        using var stack = await OnlineStack.CreateAsync();
        Assert.True((await LoginAsync(stack)).IsSuccess); // a healthy first login builds the offline authority
        Assert.NotNull((await stack.Services.GetRequiredService<ISealedEntitlementStore>().ReadAsync()).Value);

        stack.Server.DeviceRegisterFailureCode = "DEVICE_REVOKED";
        var login = await LoginAsync(stack);

        Assert.Equal(ErrorCodes.DeviceRevoked, login.Error);
        Assert.Null((await stack.Services.GetRequiredService<ISessionStore>().ReadAsync()).Value);
        Assert.Null((await stack.Services.GetRequiredService<ISealedEntitlementStore>().ReadAsync()).Value);
        Assert.Null((await stack.Services.GetRequiredService<IOfflineCredentialMirrorStore>().ReadAsync()).Value);

        var snapshot = (await stack.Services.GetRequiredService<ICapabilityStore>()
            .GetAsync(stack.Server.TenantId, stack.Server.UserId, stack.Server.DeviceId)).Value;
        Assert.True(snapshot is null || snapshot.RevokedAtUtc is not null, "capability snapshot must be revoked or gone");

        // And the wiped installation can no longer start offline.
        stack.Server.Handler.Unreachable = true;
        var offline = await LoginAsync(stack);
        Assert.True(offline.IsFailure);
        Assert.False(offline.Value?.IsOffline ?? false);
    }

    [Fact]
    public async Task PendingDevice_OnRegister_FailsClosedWithDistinctCode_AndWritesNoOfflineAuthority()
    {
        using var stack = await OnlineStack.CreateAsync();
        stack.Server.DeviceRegisterStatus = "Pending";

        var login = await LoginAsync(stack);

        Assert.Equal(ErrorCodes.DevicePendingApproval, login.Error);
        Assert.NotEqual(ErrorCodes.DeviceRevoked, login.Error);
        Assert.Null((await stack.Services.GetRequiredService<ISessionStore>().ReadAsync()).Value);
        Assert.Null((await stack.Services.GetRequiredService<ISealedEntitlementStore>().ReadAsync()).Value);
        Assert.Null((await stack.Services.GetRequiredService<IOfflineCredentialMirrorStore>().ReadAsync()).Value);

        // Without ever having been approved there is nothing to start offline from either.
        stack.Server.Handler.Unreachable = true;
        Assert.True((await LoginAsync(stack)).IsFailure);
    }

    [Fact]
    public async Task Logout_ClearsSession_ButKeepsOfflineMirror()
    {
        using var stack = await OnlineStack.CreateAsync();
        Assert.True((await LoginAsync(stack)).IsSuccess);

        var result = await stack.Client.LogoutAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(1, stack.Server.Handler.Count("auth/logout"));
        Assert.Null((await stack.Services.GetRequiredService<ISessionStore>().ReadAsync()).Value);
        Assert.NotNull((await stack.Services.GetRequiredService<IOfflineCredentialMirrorStore>().ReadAsync()).Value);
    }
    [Fact]
    public async Task TenantIsolation_LogoutWhileServerUnreachableStillClearsLocalTokens()
    {
        using var stack = await OnlineStack.CreateAsync();
        Assert.True((await LoginAsync(stack)).IsSuccess);
        stack.Server.Handler.Unreachable = true;
        await stack.Client.LogoutAsync();
        Assert.Null((await stack.Services.GetRequiredService<ISessionStore>().ReadAsync()).Value);
        Assert.NotNull((await stack.Services.GetRequiredService<IOfflineCredentialMirrorStore>().ReadAsync()).Value);
    }
}
