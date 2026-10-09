using Animora.Domain.Identity;
using Animora.Persistence.PostgreSql;
using Animora.Persistence.PostgreSql.Context;
using Animora.Persistence.PostgreSql.KeyRing;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Animora.Server.Tests.Persistence;

/// <summary>Real-PostgreSQL tests; skipped locally unless <c>ANIMORA_PG_TEST_CONNECTION</c> is set.</summary>
public sealed class PgGatedTests(PgFixture pg) : IClassFixture<PgFixture>
{
    [PgFact]
    public async Task Applies101_OnEmptyDatabase()
    {
        await using var db = TestSupport.CreateContext(connection: pg.ConnectionString);
        await db.Database.MigrateAsync();

        var applied = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        Assert.True(ServerSchemaVersion.IsExpectedHead(applied.Last()));

        await using var conn = new NpgsqlConnection(pg.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT (SELECT count(*) FROM pg_proc WHERE proname='animora_set_version')," +
            " (SELECT count(*) FROM pg_trigger WHERE tgname='trg_tenants_version')", conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(1L, reader.GetInt64(0));
        Assert.Equal(1L, reader.GetInt64(1));
    }

    [PgFact]
    public async Task Version_ChangesOnEveryUpdate_IncludingRawSql()
    {
        await using (var db = TestSupport.CreateContext(connection: pg.ConnectionString))
        {
            await db.Database.MigrateAsync();
        }

        await using var conn = new NpgsqlConnection(pg.ConnectionString);
        await conn.OpenAsync();

        await Exec(conn,
            "INSERT INTO public.\"Tenants\" (\"Id\",\"TenantId\",\"DisplayName\",\"Timezone\",\"DefaultCurrency\",\"Status\",\"UpdatedAt\") " +
            "VALUES ('T1','T1','Clinic','Asia/Tehran','IRT',0,now())");
        var v1 = await Version(conn);

        await Exec(conn, "UPDATE public.\"Tenants\" SET \"DisplayName\"='Renamed' WHERE \"Id\"='T1'");
        var v2 = await Version(conn);

        await Exec(conn, "UPDATE public.\"Tenants\" SET \"DisplayName\"='Renamed' WHERE \"Id\"='T1'");
        var v3 = await Version(conn);

        Assert.NotEqual(v1, v2);
        Assert.NotEqual(v2, v3);
    }

    [PgFact]
    public async Task KeysSurviveContextRecreation()
    {
        var options = new DbContextOptionsBuilder<AnimoraKeyRingDbContext>()
            .UseNpgsql(pg.ConnectionString, n => n.MigrationsHistoryTable(AnimoraKeyRingDbContext.MigrationsHistoryTable))
            .Options;

        await using (var first = new AnimoraKeyRingDbContext(options))
        {
            await first.Database.MigrateAsync();
            first.DataProtectionKeys.Add(new DataProtectionKey { FriendlyName = "key-1", Xml = "<key/>" });
            await first.SaveChangesAsync();
        }

        await using var second = new AnimoraKeyRingDbContext(options);
        var keys = await second.DataProtectionKeys.ToListAsync();
        Assert.Contains(keys, k => k.FriendlyName == "key-1");
    }

    private static async Task Exec(NpgsqlConnection conn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<Guid> Version(NpgsqlConnection conn)
    {
        await using var cmd = new NpgsqlCommand("SELECT \"Version\" FROM public.\"Tenants\" WHERE \"Id\"='T1'", conn);
        return (Guid)(await cmd.ExecuteScalarAsync())!;
    }
}
