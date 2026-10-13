using Animora.Domain.Results;
using Animora.Persistence.PostgreSql.Identity;
using Xunit;

namespace Animora.Server.Tests.Identity;

public sealed class RefreshTokenServiceTests
{
    [Fact]
    public async Task Rotation_IssuesNewToken_StoresOnlyHash()
    {
        var h = new AuthHarness();
        var login = (await h.Service.LoginAsync(h.Login(), "c")).Value!;

        var refreshed = await h.NewRequestScope().RefreshAsync(new(login.RefreshToken), "c2");

        Assert.True(refreshed.IsSuccess);
        Assert.NotEqual(login.RefreshToken, refreshed.Value!.RefreshToken);
        Assert.Equal(2, h.TokenStore.Rows.Count);
        Assert.Equal(h.TokenStore.Rows[0].FamilyId, h.TokenStore.Rows[1].FamilyId);
        Assert.Equal(h.TokenStore.Rows[1].Id, h.TokenStore.Rows[0].RotatedToId);

        // Only the SHA-256 hex is persisted - never the wire token itself.
        Assert.All(h.TokenStore.Rows, r =>
        {
            Assert.Equal(64, r.TokenHash.Length);
            Assert.DoesNotContain(login.RefreshToken, r.TokenHash, StringComparison.Ordinal);
            Assert.NotEqual(login.RefreshToken, r.TokenHash);
        });
        Assert.Equal(RefreshTokenService.Hash(login.RefreshToken), h.TokenStore.Rows[0].TokenHash);
        Assert.Equal(RefreshTokenService.Hash(refreshed.Value.RefreshToken), h.TokenStore.Rows[1].TokenHash);
        Assert.Equal(TimeSpan.FromDays(30), RefreshTokenService.Lifetime);
        Assert.Contains(h.Audit.Entries, e => e.Action == "auth.refresh.success");
    }

    [Fact]
    public async Task Reuse_RevokesFamily_RotatesSecurityStamp_Audits()
    {
        var h = new AuthHarness();
        var login = (await h.Service.LoginAsync(h.Login(), "c")).Value!;
        var rotated = (await h.NewRequestScope().RefreshAsync(new(login.RefreshToken), "c2")).Value!;
        string stampBefore = h.User.SecurityStamp;

        // Replaying the first (already rotated) token is reuse.
        var replay = await h.NewRequestScope().RefreshAsync(new(login.RefreshToken), "c3");

        Assert.True(replay.IsFailure);
        Assert.Equal("SESSION_EXPIRED", replay.Error.Code);
        Assert.NotEqual(stampBefore, h.User.SecurityStamp);
        Assert.All(h.TokenStore.Rows, r => Assert.NotNull(r.RevokedUtc));
        Assert.Contains(h.Audit.Entries, e => e.Action == "auth.refresh.reuse_detected" && e.CorrelationId == "c3");

        // The legitimately-rotated token of the same family is dead too.
        var afterReuse = await h.NewRequestScope().RefreshAsync(new(rotated.RefreshToken), "c4");
        Assert.Equal("SESSION_EXPIRED", afterReuse.Error.Code);
    }

    [Fact]
    public async Task Reuse_StagesRotatedStamp_BeforeCommit_AndGatesResponseOnSave()
    {
        var h = new AuthHarness();
        var login = (await h.Service.LoginAsync(h.Login(), "c")).Value!;
        await h.NewRequestScope().RefreshAsync(new(login.RefreshToken), "c2");
        string stampBefore = h.User.SecurityStamp;

        // The audit write is the commit point: the stored stamp must already be the rotated one there.
        string? stampAtCommit = null;
        h.Audit.OnWrite = e =>
        {
            if (e.Action == "auth.refresh.reuse_detected")
            {
                stampAtCommit = h.User.SecurityStamp;
            }

            return null;
        };

        var replay = await h.NewRequestScope().RefreshAsync(new(login.RefreshToken), "c3");

        Assert.Equal("SESSION_EXPIRED", replay.Error.Code);
        Assert.NotNull(stampAtCommit);
        Assert.NotEqual(stampBefore, stampAtCommit);

        // When the commit fails the response must surface the failure, not a clean SESSION_EXPIRED.
        h.Audit.OnWrite = _ => Result.Fail(ErrorCodes.ConcurrencyConflict);
        var failed = await h.NewRequestScope().RefreshAsync(new(login.RefreshToken), "c4");
        Assert.True(failed.IsFailure);
        Assert.Equal(ErrorCodes.ConcurrencyConflict.Code, failed.Error.Code);
    }

    [Fact]
    public async Task Refresh_ForRevokedDevice_Returns_DEVICE_REVOKED_AndKillsFamily()
    {
        var h = new AuthHarness();
        var login = (await h.Service.LoginAsync(h.Login(), "c")).Value!;
        h.Repository.Devices[0].Revoke("stolen", h.Clock);

        var result = await h.NewRequestScope().RefreshAsync(new(login.RefreshToken), "c2");

        Assert.Equal("DEVICE_REVOKED", result.Error.Code);
        Assert.All(h.TokenStore.Rows, r => Assert.NotNull(r.RevokedUtc));
    }

    [Fact]
    public async Task Refresh_AfterThirtyDays_IsExpired()
    {
        var h = new AuthHarness();
        var login = (await h.Service.LoginAsync(h.Login(), "c")).Value!;
        h.Clock.Advance(TimeSpan.FromDays(31));

        Assert.Equal("SESSION_EXPIRED", (await h.NewRequestScope().RefreshAsync(new(login.RefreshToken), "c2")).Error.Code);
    }

    [Fact]
    public async Task Refresh_WhileLockedOut_DoesNotBurnTheToken()
    {
        var h = new AuthHarness();
        var login = (await h.Service.LoginAsync(h.Login(), "c")).Value!;
        for (int i = 0; i < 5; i++)
        {
            await h.NewRequestScope().LoginAsync(h.Login(password: "Bad-pass-1"), "c");
        }

        Assert.Equal("USER_LOCKED_OUT", (await h.NewRequestScope().RefreshAsync(new(login.RefreshToken), "c2")).Error.Code);

        h.Clock.Advance(TimeSpan.FromMinutes(16));
        Assert.True((await h.NewRequestScope().RefreshAsync(new(login.RefreshToken), "c3")).IsSuccess);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("01ARZ3NDEKTSV4RRFFQ69G5FAV.unknown-token-value")]
    public async Task Malformed_Or_UnknownToken_Returns_SESSION_EXPIRED(string? token)
    {
        var h = new AuthHarness();

        var result = await h.Service.RefreshAsync(new(token!), "c");

        Assert.Equal("SESSION_EXPIRED", result.Error.Code);
    }

    [Fact]
    public void TryReadTenantId_ParsesPrefix_AndRejectsMalformed()
    {
        string tenant = "01ARZ3NDEKTSV4RRFFQ69G5FAV";

        Assert.Equal(tenant, RefreshTokenService.TryReadTenantId(tenant + ".abc"));
        Assert.Null(RefreshTokenService.TryReadTenantId(tenant + "-abc"));
        Assert.Null(RefreshTokenService.TryReadTenantId("short.abc"));
        Assert.Null(RefreshTokenService.TryReadTenantId(null));
    }
}
