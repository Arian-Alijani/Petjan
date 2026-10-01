using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Animora.Client.Services.Dashboard;
using Animora.Contracts.Navigation;
using Animora.Domain.Identity;
using Animora.Domain.Patients;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Entities;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Animora.Application.Tests;

/// <summary>
/// D35: opt-in, scoped evidence for the AUTHENTICATED dashboard data pipeline (authorization → generation probe →
/// dispatcher → queries on a temporary SQLite store), with explicit scope labels. It is not an xUnit performance
/// assertion and it does not measure Avalonia rendering: login→render needs a display and an interactive session,
/// which this harness never claims. Set <c>ANIMORA_DASHBOARD_EVIDENCE_OUTPUT</c> to a file path to run it.
/// </summary>
public sealed class DashboardEvidenceMeasurements(ITestOutputHelper output)
{
    private const int PatientRows = 200;

    private static double Median(IEnumerable<double> samples) => samples.Order().ElementAt(samples.Count() / 2);

    private static async Task<double> TimeAsync(Func<Task> action)
    {
        var timer = Stopwatch.StartNew();
        await action();
        return timer.Elapsed.TotalMilliseconds;
    }

    [Fact]
    public async Task CaptureAuthenticatedDashboardPipeline()
    {
        var path = Environment.GetEnvironmentVariable("ANIMORA_DASHBOARD_EVIDENCE_OUTPUT");
        if (string.IsNullOrEmpty(path)) return; // opt-in only

        var sql = new DashboardAuthorizationTests.SqlRecorder();
        await using var host = await DashboardAuthorizationTests.CreateHostAsync(StaffRole.Manager, sql);
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            var owner = Owner.Register(Animora.Domain.Identity.Tenant.LocalDefaultId, "Evidence Owner", "09120000001", host.Clock);
            db.Add(owner);
            for (var i = 0; i < PatientRows; i++)
                db.Add(Pet.Register(Animora.Domain.Identity.Tenant.LocalDefaultId, owner.Id, $"Patient-{i:000}",
                    i % 3 == 0 ? PetSpecies.Cat : PetSpecies.Dog, host.Clock));
            await db.SaveChangesAsync();
        }

        var service = new DashboardQueryService(host.Services.GetRequiredService<IServiceScopeFactory>());

        // Cold: the first authenticated read in this process (JIT, EF model/compiled query warm-up, empty cache).
        sql.Commands.Clear();
        var cold = await TimeAsync(() => service.GetSnapshotAsync(UserRole.Manager));
        var coldCommands = sql.Commands.Count;

        // Warm hit: cache hit still re-authorizes and probes the source generation (D03/D22).
        var hits = new List<double>();
        sql.Commands.Clear();
        for (var i = 0; i < 5; i++) hits.Add(await TimeAsync(() => service.GetSnapshotAsync(UserRole.Manager)));
        var hitCommands = sql.Commands.Count / 5;

        // Warm miss: forced refresh recomposes the whole snapshot.
        var misses = new List<double>();
        sql.Commands.Clear();
        for (var i = 0; i < 5; i++) misses.Add(await TimeAsync(() => service.RefreshSnapshotAsync(UserRole.Manager)));
        var missCommands = sql.Commands.Count / 5;

        // Consistency latency: ONE controlled source write, then the next read must see a new generation (miss).
        var before = await service.GetSnapshotAsync(UserRole.Manager);
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            var fact = DailyFinanceFact.Create(Animora.Domain.Identity.Tenant.LocalDefaultId, 20260301);
            fact.Apply(revenue: 1);
            db.Add(fact);
            await db.SaveChangesAsync();
        }
        var afterWrite = await TimeAsync(async () =>
        {
            var after = await service.GetSnapshotAsync(UserRole.Manager);
            Assert.NotSame(before, after); // write invalidated the cache: this read recomposed
        });

        var result = new Dictionary<string, object>
        {
            ["scope"] = "service pipeline on temporary SQLite store; NOT login-to-render, NOT Avalonia layout/render",
            ["build_configuration"] =
#if DEBUG
                "Debug",
#else
                "Release",
#endif
            ["host"] = $"{RuntimeInformation.OSDescription}; {RuntimeInformation.ProcessArchitecture}; cpus={Environment.ProcessorCount}; {RuntimeInformation.FrameworkDescription}",
            ["data_volume"] = $"{PatientRows} patients, 1 owner, 1 daily finance fact (fresh store, no activity rows)",
            ["cold_first_read_ms"] = Math.Round(cold, 3),
            ["cold_sql_commands"] = coldCommands,
            ["warm_cache_hit_median_ms"] = Math.Round(Median(hits), 3),
            ["warm_cache_hit_sql_commands"] = hitCommands,
            ["warm_forced_miss_median_ms"] = Math.Round(Median(misses), 3),
            ["warm_forced_miss_sql_commands"] = missCommands,
            ["read_after_one_write_ms"] = Math.Round(afterWrite, 3),
        };
        output.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
    }
}
