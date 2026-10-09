using Animora.Server.Auth;
using Animora.Persistence.PostgreSql.Identity;
using Xunit;

namespace Animora.Server.Tests.Identity;

public sealed class AuditRedactionTests
{
    [Theory]
    [InlineData("password")]
    [InlineData("refreshToken")]
    [InlineData("tokenHash")]
    [InlineData("apiKey")]
    [InlineData("securityStamp")]
    [InlineData("clientSecret")]
    [InlineData("credential")]
    public void SecretLookingKeys_AreRefused(string key)
    {
        Assert.Throws<ArgumentException>(() => SecurityAuditDetail.Create((key, "x")));
    }

    [Fact]
    public void NonSecretDetail_IsSerialized_AndEmptyIsNull()
    {
        Assert.Equal("{\"reason\":\"bad_password\"}", SecurityAuditDetail.Create(("reason", "bad_password")));
        Assert.Null(SecurityAuditDetail.Create());
    }

    [Fact]
    public async Task NoSecretsInDetailJson()
    {
        var h = new AuthHarness();

        // Exercise every audited path: success, failures, lockout, refresh, reuse, logout, device.
        var login = (await h.Service.LoginAsync(h.Login(), "c1")).Value!;
        await h.NewRequestScope().LoginAsync(h.Login(userName: "ghost"), "c2");
        await h.NewRequestScope().LoginAsync(h.Login(tenantCode: "nope"), "c3");
        var rotated = (await h.NewRequestScope().RefreshAsync(new(login.RefreshToken), "c4")).Value!;
        await h.NewRequestScope().RefreshAsync(new(login.RefreshToken), "c5");
        await h.NewRequestScope().LoginAsync(h.Login(password: "Wrong-Password-9"), "c6");
        h.Clock.Advance(TimeSpan.FromMinutes(1));
        var caller = new AuthenticatedCaller(login.UserId, login.TenantId, login.DeviceId, h.User.SecurityStamp);
        await h.NewRequestScope().LogoutAsync(caller, "c7");

        Assert.NotEmpty(h.Audit.Entries);

        string[] secrets =
        [
            AuthHarness.Password,
            "Wrong-Password-9",
            login.RefreshToken,
            rotated.RefreshToken,
            login.AccessToken,
            h.User.PasswordHash!,
            h.User.SecurityStamp,
            RefreshTokenService.Hash(login.RefreshToken),
            RefreshTokenService.Hash(rotated.RefreshToken),
        ];

        foreach (SecurityAuditEntry entry in h.Audit.Entries)
        {
            string haystack = string.Join('|', entry.DetailJson, entry.Action, entry.Outcome, entry.Category, entry.CorrelationId, entry.ActorUserId, entry.DeviceId);
            foreach (string secret in secrets)
            {
                Assert.DoesNotContain(secret, haystack, StringComparison.Ordinal);
            }
        }
    }
}
