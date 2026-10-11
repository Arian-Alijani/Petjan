using System.Net;
using System.Text.Json;
using Animora.Application.UseCases.Purchasing;
using Animora.Contracts.Wire.Commands;
using Animora.Persistence.PostgreSql.Context;
using Animora.Persistence.PostgreSql.KeyRing;
using Animora.Persistence.PostgreSql.Options;
using Animora.Persistence.PostgreSql.Replication;
using Animora.Server.Tests.HandlersS10;
using Animora.Server.Tests.Persistence;
using Animora.Server.Tests.Sync;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Xunit;

namespace Animora.Server.Tests.Capacity;

/// <summary>
/// ARCH §19 gate T (+ gate I back-pressure shape), G08: xUnit-hosted capacity driver for the weak-server reference
/// (1–3 core / 2–4 GiB — <c>scripts/perf/server-capacity.sh</c> runs it inside <c>--cpus=2 --memory=2g</c>).
/// It drives the two INDEPENDENT pipelines of ARCH §6 against a real PostgreSQL through the real production
/// components (not mocks): command intake (single + sequential batch, via the actual intake service + PostgreSQL
/// ports) and state pull (frozen-batch open + page download). It RECORDS throughput / p50 / p95 / p99 / peak RSS /
/// PostgreSQL connections to <c>ANIMORA_CAPACITY_OUTPUT</c> as a planning reference only (A-RULE 035) and asserts only
/// mechanical properties: zero unexpected failures, no pool exhaustion (peak connections within the S18 pool bound),
/// p99 below the server command timeout, readiness green at the end.
/// </summary>
public sealed class CapacityDriver(PgFixture pg) : IClassFixture<PgFixture>
{
    /// <summary>S18 pool bound for the two data sources the driver exercises (OLTP write + read); read from the live defaults.</summary>
    private static readonly int OltpWriteMax = new DatabaseOptions().OltpWriteMax;
    private static readonly int OltpReadMax = new DatabaseOptions().OltpReadMax;

    /// <summary>The server's per-command timeout (PersistenceServiceCollectionExtensions.CommandTimeoutSeconds = 30; private const → documented here).</summary>
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(30);

    private const string AppPrefix = "animora-capacity";
    private static readonly int[] CommandLevels = [1, 2, 4, 8];
    private static readonly int[] SyncLevels = [1, 2, 4];
    private const int CommandRequestsPerLevel = 40;
    private const int BatchRequestsPerLevel = 12;
    private const int BatchSize = 5;
    private const int SyncRequestsPerLevel = 12;

    private string Pooled(string app, int max) => new NpgsqlConnectionStringBuilder(pg.ConnectionString)
    {
        Pooling = true,
        MinPoolSize = 0,
        MaxPoolSize = max,
        ApplicationName = $"{AppPrefix}-{app}",
        Timeout = 30,
    }.ConnectionString;

    [PgFact]
    public async Task Capacity_TwoPipelines_NoExhaustion_NoUnexpected5xx_ReadyAtEnd()
    {
        // Schema as OWNER (the running role never migrates): server + key-ring contexts on the fixture database.
        await using (AnimoraServerDbContext migrate = TestSupport.CreateContext(connection: pg.ConnectionString))
        {
            await migrate.Database.MigrateAsync();
        }

        await using (var keyRing = new AnimoraKeyRingDbContext(new DbContextOptionsBuilder<AnimoraKeyRingDbContext>()
            .UseNpgsql(pg.ConnectionString, n => n.MigrationsHistoryTable(AnimoraKeyRingDbContext.MigrationsHistoryTable)).Options))
        {
            await keyRing.Database.MigrateAsync();
        }

        string version = await ScalarAsync(pg.ConnectionString, "SHOW server_version");
        string oltpConnection = Pooled("oltp", OltpWriteMax);
        string syncConnection = Pooled("sync", OltpReadMax);
        var profiles = new List<ProfileResult>();

        await using var sampler = new ResourceSampler(pg.ConnectionString, AppPrefix);

        // ── command-intake pipeline ──────────────────────────────────────────────────────────────────────────
        int maxWorkers = CommandLevels.Max();
        var harnesses = new List<PgCommandHarness>();
        try
        {
            for (int i = 0; i < maxWorkers; i++)
            {
                harnesses.Add(new PgCommandHarness(oltpConnection));
            }

            await using (AnimoraServerDbContext seedDb = TestSupport.CreateContext(connection: pg.ConnectionString))
            {
                await seedDb.MigrateAndSeedAsync(harnesses.Select(h => h.Intake.TenantId).ToArray());
            }

            foreach (int concurrency in CommandLevels)
            {
                profiles.Add(await LoadRunner.RunAsync("command-intake-single", concurrency, CommandRequestsPerLevel, async (worker, i) =>
                {
                    PgCommandHarness h = harnesses[worker];
                    var result = await h.Service.SubmitAsync(h.Envelope(new CreateSupplierCommand($"cap-{concurrency}-{worker}-{i}-{Guid.NewGuid():N}")));
                    return result.IsSuccess && result.Value!.Outcome == SettlementOutcome.Accepted ? Outcome.Ok : Outcome.Unexpected;
                }));
            }

            foreach (int concurrency in CommandLevels)
            {
                // Mirrors POST /api/commands/batch: sequential items, one intake transaction each — never WhenAll.
                profiles.Add(await LoadRunner.RunAsync("command-intake-batch" + BatchSize, concurrency, BatchRequestsPerLevel, async (worker, i) =>
                {
                    PgCommandHarness h = harnesses[worker];
                    for (int k = 0; k < BatchSize; k++)
                    {
                        var result = await h.Service.SubmitAsync(h.Envelope(new CreateSupplierCommand($"capb-{concurrency}-{worker}-{i}-{k}-{Guid.NewGuid():N}")));
                        if (!result.IsSuccess || result.Value!.Outcome != SettlementOutcome.Accepted)
                        {
                            return Outcome.Unexpected;
                        }
                    }

                    return Outcome.Ok;
                }));
            }
        }
        finally
        {
            foreach (PgCommandHarness h in harnesses)
            {
                await h.DisposeAsync();
            }
        }

        // ── state-pull pipeline (independent of intake) ──────────────────────────────────────────────────────
        await using (var rig = new SyncPgRig(syncConnection))
        {
            await rig.Initialize();
            foreach (int concurrency in SyncLevels)
            {
                profiles.Add(await LoadRunner.RunAsync("state-pull", concurrency, SyncRequestsPerLevel, async (_, _) =>
                {
                    try
                    {
                        var opened = await rig.Freezer.OpenAsync(new(0, 0), true, CancellationToken.None);
                        if (opened.Batch is null)
                        {
                            return Outcome.Unexpected;
                        }

                        List<Animora.Contracts.Wire.Sync.SyncRecord> records = await rig.Download(opened.Batch);
                        return records.Count > 0 ? Outcome.Ok : Outcome.Unexpected;
                    }
                    catch (SnapshotLifetimeExceededException)
                    {
                        // Documented back-pressure (HTTP 503 + Retry-After: 1), not a defect (gate I shape).
                        return Outcome.ExpectedBusy;
                    }
                }));
            }
        }

        // ── readiness green at the end (real host over the same, fully migrated database) ───────────────────
        string readiness = await ReadinessAsync();

        await sampler.DisposeAsync();
        int unexpected = profiles.Sum(p => p.UnexpectedFailures);
        double worstP99 = profiles.Max(p => p.P99);
        int bound = OltpWriteMax + OltpReadMax;

        var report = new CapacityReport(
            "PLANNING REFERENCE ONLY (A-RULE 035) — recorded measurement, never a guaranteed user count",
            Environment.GetEnvironmentVariable("ANIMORA_CAPACITY_CPUS") ?? "unspecified",
            Environment.GetEnvironmentVariable("ANIMORA_CAPACITY_MEMORY") ?? "unspecified",
            version,
            Math.Round(sampler.PeakRssMiB, 1),
            sampler.PeakConnections,
            bound,
            CommandTimeout.TotalMilliseconds,
            unexpected,
            readiness,
            profiles);
        WriteReport(report);

        // Mechanical assertions only.
        Assert.Equal(0, unexpected);
        Assert.True(sampler.PeakConnections <= bound,
            $"pool exhaustion: peak harness connections {sampler.PeakConnections} exceed the S18 bound {bound} (oltp-write {OltpWriteMax} + oltp-read {OltpReadMax})");
        Assert.True(worstP99 < CommandTimeout.TotalMilliseconds,
            $"p99 {worstP99:F0} ms is not below the server command timeout {CommandTimeout.TotalMilliseconds:F0} ms");
        Assert.Equal("ready", readiness);
    }

    private static void WriteReport(CapacityReport report)
    {
        string path = Environment.GetEnvironmentVariable("ANIMORA_CAPACITY_OUTPUT")
            ?? Path.Combine(RepoRoot.Find(), "artifacts", "server-capacity.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static async Task<string> ScalarAsync(string connection, string sql)
    {
        await using var conn = new NpgsqlConnection(connection);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        return (string)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task<string> ReadinessAsync()
    {
        string root = Directory.CreateTempSubdirectory("animora-capacity-").FullName;
        try
        {
            using WebApplicationFactory<Program> factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Server:DataRootPath"] = root,
                    ["Database:ConnectionString"] = pg.ConnectionString,
                    ["Telemetry:Enabled"] = "false",
                }));
                builder.ConfigureServices(s =>
                {
                    foreach (ServiceDescriptor d in s.Where(d => d.ServiceType == typeof(IHostedService)
                        && (d.ImplementationFactory is not null || d.ImplementationType?.Assembly == typeof(Program).Assembly)).ToArray())
                    {
                        s.Remove(d);
                    }
                });
            });
            using HttpClient client = factory.CreateClient();
            for (int i = 0; i < 20; i++)
            {
                using HttpResponseMessage r = await client.GetAsync("/health/ready");
                string body = await r.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(body);
                string status = doc.RootElement.GetProperty("status").GetString() ?? "unknown";
                if (r.StatusCode == HttpStatusCode.OK)
                {
                    // "degraded" (optional worker/files probes only) is still HTTP 200; the gate requires no REQUIRED probe failing.
                    return status == "ready" || status == "degraded" ? "ready" : status;
                }

                await Task.Delay(500);
            }

            return "not-ready";
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
