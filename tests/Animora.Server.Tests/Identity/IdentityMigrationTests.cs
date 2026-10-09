using Animora.Domain.Identity;
using Animora.Domain.Time;
using Animora.Persistence.PostgreSql;
using Animora.Persistence.PostgreSql.Context;
using Animora.Persistence.PostgreSql.Identity;
using Animora.Server.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Animora.Server.Tests.Identity;

/// <summary>Real-PostgreSQL checks of the <c>102_Identity</c> migration; skipped unless a test server is configured.</summary>
public sealed class IdentityMigrationTests(PgFixture pg) : IClassFixture<PgFixture>
{
    [PgFact]
    public async Task SecurityAudit_UpdateAndDelete_Raise()
    {
        var host = await IdentityPgHost.GetAsync(pg.ConnectionString);
        var provisioned = await Provision(host, "audit-clinic", "owner");

        await using (var scope = host.Scope())
        {
            scope.ServiceProvider.GetRequiredService<AuthTenantContext>().Bind(provisioned.TenantId);
            var clock = scope.ServiceProvider.GetRequiredService<IClock>();
            var writer = scope.ServiceProvider.GetRequiredService<ISecurityAuditWriter>();
            var entry = SecurityAuditEntry.Create(
                Animora.Domain.Primitives.IdGenerator.NewId(), provisioned.TenantId, clock.UtcNow, "auth", "auth.login.success", "success", "corr-1");
            Assert.True((await writer.WriteAsync(entry)).IsSuccess);
        }

        await using var conn = new NpgsqlConnection(pg.ConnectionString);
        await conn.OpenAsync();

        var update = await Assert.ThrowsAsync<PostgresException>(() => Exec(conn, "UPDATE public.\"SecurityAudit\" SET \"Outcome\"='tampered'"));
        Assert.Contains("append-only", update.MessageText, StringComparison.OrdinalIgnoreCase);

        var delete = await Assert.ThrowsAsync<PostgresException>(() => Exec(conn, "DELETE FROM public.\"SecurityAudit\""));
        Assert.Contains("append-only", delete.MessageText, StringComparison.OrdinalIgnoreCase);

        await Assert.ThrowsAsync<PostgresException>(() => Exec(conn, "TRUNCATE public.\"SecurityAudit\""));

        await using var count = new NpgsqlCommand("SELECT count(*) FROM public.\"SecurityAudit\" WHERE \"Outcome\"='success'", conn);
        Assert.Equal(1L, (long)(await count.ExecuteScalarAsync())!);
    }

    [PgFact]
    public async Task Users_UniquePerTenantUsername()
    {
        var host = await IdentityPgHost.GetAsync(pg.ConnectionString);
        var provisioned = await Provision(host, "unique-clinic", "dup");

        await using var scope = host.Scope();
        scope.ServiceProvider.GetRequiredService<AuthTenantContext>().Bind(provisioned.TenantId);
        var db = scope.ServiceProvider.GetRequiredService<AnimoraServerDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        db.Users.Add(User.Create(provisioned.TenantId, "DUP", "Second", clock));

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal("23505", Assert.IsType<PostgresException>(ex.InnerException).SqlState);
    }

    [PgFact]
    public async Task Migration_Applies_To_Head_And_Installs_Triggers_AndTenantCode()
    {
        var host = await IdentityPgHost.GetAsync(pg.ConnectionString);

        await using var conn = new NpgsqlConnection(pg.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT (SELECT count(*) FROM pg_trigger WHERE tgname IN ('trg_users_version','trg_staffmemberships_version','trg_roledefinitions_version','trg_devices_version'))," +
            " (SELECT count(*) FROM pg_trigger WHERE tgname='trg_securityaudit_immutable')," +
            " (SELECT count(*) FROM pg_indexes WHERE indexname='UX_Tenants_Code')," +
            " (SELECT \"MigrationId\" FROM public.\"__EFMigrationsHistory\" ORDER BY \"MigrationId\" DESC LIMIT 1)", conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(4L, reader.GetInt64(0));
        Assert.Equal(1L, reader.GetInt64(1));
        Assert.Equal(1L, reader.GetInt64(2));
        Assert.True(ServerSchemaVersion.IsExpectedHead(reader.GetString(3)));
        Assert.Equal("103_SigningKeys", ServerSchemaVersion.ExpectedHead);
    }

    internal static async Task<ProvisionedTenant> Provision(IdentityPgHost host, string code, string owner)
    {
        await using var scope = host.Scope();
        var provisioner = scope.ServiceProvider.GetRequiredService<TenantProvisioner>();
        var result = await provisioner.CreateTenantWithOwnerAsync("Clinic " + code, code, owner, "Owner", "Sup3r-secret-pw");
        Assert.True(result.IsSuccess, result.Error.Code + " " + result.Detail);
        return result.Value!;
    }

    private static async Task Exec(NpgsqlConnection conn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }
}
