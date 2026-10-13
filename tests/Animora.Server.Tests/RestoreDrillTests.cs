using System.Net;
using System.Security.Cryptography;
using Animora.Persistence.PostgreSql.Context;
using Animora.Persistence.PostgreSql.KeyRing;
using Animora.Server.Tests.Persistence;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Xunit;

namespace Animora.Server.Tests;

/// <summary>
/// ARCH §17.3 / gate R + K, in-process twin of <c>scripts/ci/restore-drill.sh</c>: a restored
/// set (server DB + file tree + key ring) must come up ready with the known tenant, key and file
/// intact, while a set restored WITHOUT the key ring must stay live but never ready (A-RULE 027).
/// "Restore" here is a logical copy of the known rows onto freshly migrated clean databases; the
/// shell drill covers the physical pg_dump/pg_restore path and the RTO measurement.
/// </summary>
public sealed class RestoreDrillTests(PgFixture pg) : IClassFixture<PgFixture>
{
    private const string TenantId = "DRILL-T1";
    private const string TenantName = "Drill Clinic";
    private const string KeyName = "drill-key";

    private static async Task ExecAsync(string connection, string sql)
    {
        await using var conn = new NpgsqlConnection(connection);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        _ = await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<T> ScalarAsync<T>(string connection, string sql)
    {
        await using var conn = new NpgsqlConnection(connection);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        return (T)(await cmd.ExecuteScalarAsync())!;
    }

    private static async Task MigrateAsync(string connection, bool keyRing)
    {
        await using (AnimoraServerDbContext db = TestSupport.CreateContext(connection: connection))
        {
            await db.Database.MigrateAsync();
        }

        if (keyRing)
        {
            var options = new DbContextOptionsBuilder<AnimoraKeyRingDbContext>()
                .UseNpgsql(connection, n => n.MigrationsHistoryTable(AnimoraKeyRingDbContext.MigrationsHistoryTable))
                .Options;
            await using var kr = new AnimoraKeyRingDbContext(options);
            await kr.Database.MigrateAsync();
        }
    }

    /// <summary>Creates an extra clean database on the fixture's server; dropped by the caller.</summary>
    private static async Task<string> CreateCleanDatabaseAsync(string baseConnection, string name)
    {
        string admin = PgFixture.AdminConnectionFromEnv ?? baseConnection;
        await ExecAsync(admin, $"CREATE DATABASE \"{name}\"");
        return new NpgsqlConnectionStringBuilder(admin) { Database = name, Pooling = false }.ConnectionString;
    }

    private static async Task DropDatabaseAsync(string name)
    {
        NpgsqlConnection.ClearAllPools();
        await ExecAsync(PgFixture.AdminConnectionFromEnv!, $"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)");
    }

    private static WebApplicationFactory<Program> CreateHost(string connection, string dataRoot)
        => new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Server:DataRootPath"] = dataRoot,
                ["Database:ConnectionString"] = connection,
                ["Telemetry:Enabled"] = "false",
                ["Server:Health:ReadinessCacheSeconds"] = "0", // the drill observes state transitions; never serve a cached verdict
            }));
            builder.ConfigureServices(s =>
            {
                // Isolate readiness from background workers (each has its own gate).
                foreach (ServiceDescriptor d in s.Where(d => d.ServiceType == typeof(IHostedService)
                    && (d.ImplementationFactory is not null || d.ImplementationType?.Assembly == typeof(Program).Assembly)).ToArray())
                {
                    s.Remove(d);
                }
            });
        });

    private static async Task<HttpStatusCode> ReadyAsync(HttpClient client, HttpStatusCode expected)
    {
        HttpStatusCode last = default;
        for (int i = 0; i < 30; i++)
        {
            using HttpResponseMessage r = await client.GetAsync("/health/ready");
            last = r.StatusCode;
            if (last == expected)
            {
                break;
            }

            await Task.Delay(500);
        }

        return last;
    }

    [PgFact]
    public async Task Restore_ToReadiness_KnownTenantIntact()
    {
        // Source: migrated set with one tenant, one key-ring key and one stored file.
        await MigrateAsync(pg.ConnectionString, keyRing: true);
        await ExecAsync(pg.ConnectionString,
            $"INSERT INTO public.\"Tenants\" (\"Id\",\"TenantId\",\"DisplayName\",\"Timezone\",\"DefaultCurrency\",\"Status\",\"UpdatedAt\") " +
            $"VALUES ('{TenantId}','{TenantId}','{TenantName}','Asia/Tehran','IRT',0,now())");
        await ExecAsync(pg.ConnectionString,
            $"INSERT INTO public.\"DataProtectionKeys\" (\"FriendlyName\",\"Xml\") VALUES ('{KeyName}','<key/>')");

        string srcRoot = Directory.CreateTempSubdirectory("animora-restore-src-").FullName;
        string dstRoot = Directory.CreateTempSubdirectory("animora-restore-dst-").FullName;
        string dstDb = "animora_restore_" + Guid.NewGuid().ToString("N");
        try
        {
            string fileRel = Path.Combine("files", TenantId, "known.bin");
            string srcFile = Path.Combine(srcRoot, fileRel);
            string dstFile = Path.Combine(dstRoot, fileRel);
            Directory.CreateDirectory(Path.GetDirectoryName(srcFile)!);
            await File.WriteAllBytesAsync(srcFile, RandomNumberGenerator.GetBytes(4096));
            string expectedHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(srcFile)));

            // Restore the THREE parts onto a clean database + directory.
            string dstConn = await CreateCleanDatabaseAsync(pg.ConnectionString, dstDb);
            await MigrateAsync(dstConn, keyRing: true);
            await CopyKnownRowsAsync(pg.ConnectionString, dstConn);
            Directory.CreateDirectory(Path.GetDirectoryName(dstFile)!);
            File.Copy(srcFile, dstFile);

            using WebApplicationFactory<Program> factory = CreateHost(dstConn, dstRoot);
            using HttpClient client = factory.CreateClient();

            using (HttpResponseMessage live = await client.GetAsync("/health/live"))
            {
                Assert.Equal(HttpStatusCode.OK, live.StatusCode);
            }

            Assert.Equal(HttpStatusCode.OK, await ReadyAsync(client, HttpStatusCode.OK));

            Assert.Equal(1L, await ScalarAsync<long>(dstConn,
                $"SELECT count(*) FROM public.\"Tenants\" WHERE \"Id\"='{TenantId}' AND \"DisplayName\"='{TenantName}'"));
            Assert.Equal(1L, await ScalarAsync<long>(dstConn,
                $"SELECT count(*) FROM public.\"DataProtectionKeys\" WHERE \"FriendlyName\"='{KeyName}'"));
            Assert.Equal(expectedHash, Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(dstFile))));
        }
        finally
        {
            await DropDatabaseAsync(dstDb);
            Directory.Delete(srcRoot, true);
            Directory.Delete(dstRoot, true);
        }
    }

    [PgFact]
    public async Task MissingKeyRing_RefusesReadiness_ServesLiveness()
    {
        string db = "animora_nokey_" + Guid.NewGuid().ToString("N");
        string root = Directory.CreateTempSubdirectory("animora-nokey-").FullName;
        try
        {
            // Server schema restored, key-ring tables deliberately absent (the forgotten third part).
            string conn = await CreateCleanDatabaseAsync(pg.ConnectionString, db);
            await MigrateAsync(conn, keyRing: false);

            using WebApplicationFactory<Program> factory = CreateHost(conn, root);
            using HttpClient client = factory.CreateClient();

            using (HttpResponseMessage live = await client.GetAsync("/health/live"))
            {
                Assert.Equal(HttpStatusCode.OK, live.StatusCode);
            }

            Assert.Equal(HttpStatusCode.ServiceUnavailable, await ReadyAsync(client, HttpStatusCode.ServiceUnavailable));

            using HttpResponseMessage ready = await client.GetAsync("/health/ready");
            string body = await ready.Content.ReadAsStringAsync();
            Assert.Contains("keyring", body, StringComparison.Ordinal);

            // Business surface is refused (no authenticated principal can exist without a key ring).
            using HttpResponseMessage biz = await client.PostAsync("/api/commands", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
            Assert.Contains(biz.StatusCode, new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden, HttpStatusCode.ServiceUnavailable });
        }
        finally
        {
            await DropDatabaseAsync(db);
            Directory.Delete(root, true);
        }
    }

    private static async Task CopyKnownRowsAsync(string srcConn, string dstConn)
    {
        await using var src = new NpgsqlConnection(srcConn);
        await src.OpenAsync();
        await using var dst = new NpgsqlConnection(dstConn);
        await dst.OpenAsync();

        await using (var read = new NpgsqlCommand(
            "SELECT \"Id\",\"TenantId\",\"DisplayName\",\"Timezone\",\"DefaultCurrency\",\"Status\",\"UpdatedAt\" FROM public.\"Tenants\" WHERE \"Id\" = @id", src))
        {
            read.Parameters.AddWithValue("id", TenantId);
            await using NpgsqlDataReader r = await read.ExecuteReaderAsync();
            Assert.True(await r.ReadAsync());
            await using var write = new NpgsqlCommand(
                "INSERT INTO public.\"Tenants\" (\"Id\",\"TenantId\",\"DisplayName\",\"Timezone\",\"DefaultCurrency\",\"Status\",\"UpdatedAt\") VALUES (@p0,@p1,@p2,@p3,@p4,@p5,@p6)", dst);
            for (int i = 0; i < 7; i++)
            {
                write.Parameters.AddWithValue("p" + i, r.GetValue(i));
            }

            _ = await write.ExecuteNonQueryAsync();
        }

        await using (var readKey = new NpgsqlCommand("SELECT \"FriendlyName\",\"Xml\" FROM public.\"DataProtectionKeys\" WHERE \"FriendlyName\" = @n", src))
        {
            readKey.Parameters.AddWithValue("n", KeyName);
            await using NpgsqlDataReader r = await readKey.ExecuteReaderAsync();
            Assert.True(await r.ReadAsync());
            await using var write = new NpgsqlCommand("INSERT INTO public.\"DataProtectionKeys\" (\"FriendlyName\",\"Xml\") VALUES (@n,@x)", dst);
            write.Parameters.AddWithValue("n", r.GetString(0));
            write.Parameters.AddWithValue("x", r.GetString(1));
            _ = await write.ExecuteNonQueryAsync();
        }
    }
}
