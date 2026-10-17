using Animora.Contracts.Wire.Auth;
using Animora.Domain.Results;
using Animora.Persistence.PostgreSql.Context;
using Animora.Persistence.PostgreSql.Identity;
using Animora.Server.Auth;
using Animora.Server.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Animora.Server.Tests.Identity;

/// <summary>End-to-end authentication over real PostgreSQL (one DI scope per simulated request).</summary>
public sealed class AuthFlowTests(PgFixture pg) : IClassFixture<PgFixture>
{
    private const string Password = "Sup3r-secret-pw";

    private static async Task<Result<LoginResponse>> LoginAsync(IdentityPgHost host, string tenant, string user, string password, string installation = "inst-1")
    {
        await using var scope = host.Scope();
        return await scope.ServiceProvider.GetRequiredService<LoginService>()
            .LoginAsync(new LoginRequest(tenant, user, password, installation), "corr-login");
    }

    private static async Task<Result<LoginResponse>> RefreshAsync(IdentityPgHost host, string token)
    {
        await using var scope = host.Scope();
        return await scope.ServiceProvider.GetRequiredService<LoginService>().RefreshAsync(new RefreshRequest(token), "corr-refresh");
    }

    [PgFact]
    public async Task Login_Refresh_Logout_EndToEnd()
    {
        var host = await IdentityPgHost.GetAsync(pg.ConnectionString);
        var tenant = await IdentityMigrationTests.Provision(host, "e2e-clinic", "alice");

        // Wrong password never reveals anything; correct credentials sign in and bind an auto-approved Active device.
        Assert.Equal("CREDENTIALS_INVALID", (await LoginAsync(host, "E2E-Clinic", "alice", "Wrong-pass-1")).Error.Code);
        var login = await LoginAsync(host, "E2E-Clinic", "ALICE", Password);
        Assert.True(login.IsSuccess, login.Error.Code);
        Assert.Equal("Manager", login.Value!.Role);
        Assert.Equal(tenant.TenantId, login.Value.TenantId);

        // Refresh rotates; the old token replayed is reuse and kills the family.
        var rotated = await RefreshAsync(host, login.Value.RefreshToken);
        Assert.True(rotated.IsSuccess, rotated.Error.Code);
        Assert.NotEqual(login.Value.RefreshToken, rotated.Value!.RefreshToken);
        Assert.Equal("SESSION_EXPIRED", (await RefreshAsync(host, login.Value.RefreshToken)).Error.Code);
        Assert.Equal("SESSION_EXPIRED", (await RefreshAsync(host, rotated.Value.RefreshToken)).Error.Code);

        // Sign in again, then log out: the refresh token is revoked.
        var second = (await LoginAsync(host, "e2e-clinic", "alice", Password)).Value!;
        string stamp = await StampAsync(host, tenant.TenantId, second.UserId);
        await using (var scope = host.Scope())
        {
            var caller = new AuthenticatedCaller(second.UserId, second.TenantId, second.DeviceId, stamp);
            var logout = await scope.ServiceProvider.GetRequiredService<LoginService>().LogoutAsync(caller, "corr-out");
            Assert.True(logout.IsSuccess, logout.Error.Code);
        }

        Assert.Equal("SESSION_EXPIRED", (await RefreshAsync(host, second.RefreshToken)).Error.Code);

        // Device registration for the authenticated caller.
        await using (var scope = host.Scope())
        {
            var caller = new AuthenticatedCaller(second.UserId, second.TenantId, second.DeviceId, stamp);
            var registered = await scope.ServiceProvider.GetRequiredService<LoginService>()
                .RegisterDeviceAsync(caller, new DeviceRegisterRequest("Reception PC", "inst-2", "windows"), "corr-dev");
            Assert.True(registered.IsSuccess, registered.Error.Code);
            Assert.Equal("Pending", registered.Value!.Status);
        }

        // Audit trail was written, append-only, and contains no secret.
        await using var conn = new NpgsqlConnection(pg.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT \"Action\", coalesce(\"DetailJson\"::text,'') FROM public.\"SecurityAudit\" WHERE \"TenantId\"=@t", conn);
        cmd.Parameters.AddWithValue("t", tenant.TenantId);
        var actions = new List<string>();
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                actions.Add(reader.GetString(0));
                Assert.DoesNotContain(Password, reader.GetString(1), StringComparison.Ordinal);
                Assert.DoesNotContain(login.Value.RefreshToken, reader.GetString(1), StringComparison.Ordinal);
            }
        }

        Assert.Contains("auth.login.success", actions);
        Assert.Contains("auth.login.failure", actions);
        Assert.Contains("auth.refresh.success", actions);
        Assert.Contains("auth.refresh.reuse_detected", actions);
        Assert.Contains("auth.logout", actions);
        Assert.Contains("auth.device.registered", actions);

        // Only the SHA-256 of the wire token is stored.
        await using var tokens = new NpgsqlCommand("SELECT count(*) FROM public.\"RefreshTokens\" WHERE \"TokenHash\" = @w", conn);
        tokens.Parameters.AddWithValue("w", login.Value.RefreshToken);
        Assert.Equal(0L, (long)(await tokens.ExecuteScalarAsync())!);
    }

    [PgFact]
    public async Task CrossTenant_Username_Reuse_Allowed()
    {
        var host = await IdentityPgHost.GetAsync(pg.ConnectionString);
        var a = await IdentityMigrationTests.Provision(host, "tenant-a", "shared");
        var b = await IdentityMigrationTests.Provision(host, "tenant-b", "shared");
        Assert.NotEqual(a.OwnerUserId, b.OwnerUserId);

        var loginA = await LoginAsync(host, "tenant-a", "shared", Password);
        var loginB = await LoginAsync(host, "tenant-b", "shared", Password);

        Assert.True(loginA.IsSuccess, loginA.Error.Code);
        Assert.True(loginB.IsSuccess, loginB.Error.Code);
        Assert.Equal(a.TenantId, loginA.Value!.TenantId);
        Assert.Equal(b.TenantId, loginB.Value!.TenantId);
        Assert.Equal(a.OwnerUserId, loginA.Value.UserId);
        Assert.Equal(b.OwnerUserId, loginB.Value.UserId);

        // A tenant-A refresh token can never be redeemed against tenant B's data.
        Assert.NotEqual(loginA.Value.RefreshToken[..26], loginB.Value.RefreshToken[..26]);
    }

    [PgFact]
    public async Task Lockout_Persists_AcrossRequests()
    {
        var host = await IdentityPgHost.GetAsync(pg.ConnectionString);
        await IdentityMigrationTests.Provision(host, "lock-clinic", "bob");

        for (int i = 0; i < 4; i++)
        {
            Assert.Equal("CREDENTIALS_INVALID", (await LoginAsync(host, "lock-clinic", "bob", "Wrong-pass-1")).Error.Code);
        }

        Assert.Equal("USER_LOCKED_OUT", (await LoginAsync(host, "lock-clinic", "bob", "Wrong-pass-1")).Error.Code);
        Assert.Equal("USER_LOCKED_OUT", (await LoginAsync(host, "lock-clinic", "bob", Password)).Error.Code);
    }

    private static async Task<string> StampAsync(IdentityPgHost host, string tenantId, string userId)
    {
        await using var scope = host.Scope();
        scope.ServiceProvider.GetRequiredService<AuthTenantContext>().Bind(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<AnimoraServerDbContext>();
        return (await db.Users.AsNoTracking().FirstAsync(u => u.Id == userId)).SecurityStamp;
    }
}
