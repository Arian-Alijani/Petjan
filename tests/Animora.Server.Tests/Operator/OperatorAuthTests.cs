using System.Security.Claims;
using Animora.Domain.Results;
using Animora.Persistence.PostgreSql.Operator;
using Animora.Server.Authorization;
using Animora.Server.Operator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Xunit;

namespace Animora.Server.Tests.Operator;

public sealed class OperatorAuthTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Lockout_After5Failures()
    {
        var account = new OperatorAccount();
        for (var i = 0; i < 4; i++) { account.FailLogin(Now); Assert.False(account.IsLockedOut(Now)); }
        account.FailLogin(Now); Assert.True(account.IsLockedOut(Now));
        Assert.Equal(Now.AddMinutes(15), account.LockoutEndUtc);
        Assert.False(account.IsLockedOut(Now.AddMinutes(15)));
        account.FailLogin(Now.AddMinutes(16)); Assert.Equal(1, account.FailedAttempts);
        account.SucceedLogin(Now.AddMinutes(17)); Assert.Equal(0, account.FailedAttempts); Assert.Null(account.LockoutEndUtc);
    }

    [Fact]
    public async Task TenantToken_CannotCallOperator_And_OperatorToken_CannotCallTenantApi()
    {
        var binding = new OperatorTenantContext(); binding.Authenticate();
        var requirement = new OperatorOnlyRequirement();
        var handler = new OperatorOnlyHandler(binding);
        var tenant = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "operator")], IdentityConstants.BearerScheme));
        var tenantAuth = new AuthorizationHandlerContext([requirement], tenant, null);
        await ((IAuthorizationHandler)handler).HandleAsync(tenantAuth);
        Assert.False(tenantAuth.HasSucceeded);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "operator")], OperatorAuth.Scheme));
        var operatorAuth = new AuthorizationHandlerContext([requirement], principal, null);
        await ((IAuthorizationHandler)handler).HandleAsync(operatorAuth);
        Assert.True(operatorAuth.HasSucceeded);
        var member = new DeviceActiveRequirement();
        var tenantState = new RequestAuthorizationState();
        var memberContext = new AuthorizationHandlerContext([member], principal, null);
        await ((IAuthorizationHandler)new DeviceActiveHandler(tenantState)).HandleAsync(memberContext);
        Assert.False(memberContext.HasSucceeded);
    }

    [Fact]
    public void PasswordChange_RotatesStamp_ClearsLockout_AndForcesRelogin()
    {
        var account = new OperatorAccount { SecurityStamp = "old", MustChangePassword = true };
        for (var i = 0; i < 5; i++) account.FailLogin(Now);
        account.SetPassword("encoded-password-hash", Now);
        Assert.NotEqual("old", account.SecurityStamp); Assert.Equal(26, account.SecurityStamp.Length);
        Assert.False(account.MustChangePassword); Assert.False(account.IsLockedOut(Now));
        Assert.Equal(TimeSpan.FromMinutes(30), OperatorAuth.Lifetime);
    }

    [Fact]
    public void UnknownAndWrongPassword_TransportError_IsIdentical()
    {
        // Both expected authentication failures expose the one code, never the account/lockout reason.
        Assert.Equal("OPERATOR_CREDENTIALS_INVALID", ErrorCodes.OperatorCredentialsInvalid.Code);
        var source = File.ReadAllText(Path.Combine(RepoRoot.Find(), "src", "Animora.Server", "Operator", "OperatorAccountService.cs"));
        Assert.Contains("account ?? dummy.Account", source);
        Assert.Contains("account?.PasswordHash ?? dummy.Hash", source);
        Assert.DoesNotContain("ErrorCodes.OperatorLockedOut", source);
    }

    [Fact]
    public void MustChangePassword_BlocksOtherCalls()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot.Find(), "src", "Animora.Server", "Operator", "OperatorContextMiddleware.cs"));
        Assert.Contains("account.MustChangePassword", source);
        Assert.Contains("/auth/change-password", source);
        Assert.Contains("http.AuthenticateAsync(OperatorAuth.Scheme)", source);
        Assert.Contains("account.SecurityStamp != stamp", source);
    }
}
