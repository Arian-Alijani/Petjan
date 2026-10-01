using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Animora.Application.UseCases.Dashboard;
using Animora.Contracts.Navigation;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Entities;
using Animora.Persistence.Sqlite.Queries;
using Animora.Persistence.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Animora.Application.Tests;

/// <summary>
/// D23: the real EF SQL of every final dashboard widget statement, run through EXPLAIN QUERY PLAN on a small
/// deterministic two-tenant fixture. Plan evidence (index use, no tenant-wide history scan), not a latency claim.
/// </summary>
public sealed class DashboardPlanTests
{
    private static DashboardDayWindow Window => DashboardDayWindow.For(new DateOnly(2026, 3, 1), TimeZoneInfo.Utc);

    private sealed class Recorder : DbCommandInterceptor
    {
        public List<(string Sql, Dictionary<string, object?> Args)> Commands { get; } = [];
        private void Record(DbCommand c)
        {
            var args = new Dictionary<string, object?>();
            foreach (DbParameter p in c.Parameters) args[p.ParameterName] = p.Value;
            Commands.Add((c.CommandText, args));
        }
        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand c, CommandEventData d, InterceptionResult<DbDataReader> r)
        { Record(c); return r; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand c, CommandEventData d,
            InterceptionResult<DbDataReader> r, CancellationToken ct = default)
        { Record(c); return ValueTask.FromResult(r); }
    }

    private static async Task SeedAsync(LocalStoreTestFixture fixture)
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        for (var i = 0; i < 600; i++)
            db.Add(ActivityEvent.Create(i % 2 == 0 ? fixture.TenantId : "OTHER-TENANT", fixture.Clock.UtcNow, 20260301 - i % 3,
                "Visit", "history.visit.completed", "{}", "Visit", $"v-{i}", 0, 0, "Done", $"doc-{i % 4}", "n", null));
        await db.SaveChangesAsync();
    }

    private static async Task<List<(string Sql, List<string> Plan)>> PlansAsync(LocalStoreTestFixture fixture, UserRole role)
    {
        var recorder = new Recorder();
        await using var db = new AnimoraLocalDbContext(new DbContextOptionsBuilder<AnimoraLocalDbContext>()
            .UseSqlite($"Data Source={fixture.DatabasePath}").AddInterceptors(recorder).Options,
            new FixedTenantContext(fixture.TenantId), fixture.Clock);
        var access = new DashboardAccess(role, true, true, true, true, true, true, true, true, true, true);
        await new DashboardQueries(db).GetSliceAsync(fixture.TenantId, Window, "doc-1", access, default);

        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={fixture.DatabasePath}");
        await connection.OpenAsync();
        var result = new List<(string, List<string>)>();
        foreach (var (sql, args) in recorder.Commands)
        {
            await using var explain = connection.CreateCommand();
            explain.CommandText = "EXPLAIN QUERY PLAN " + sql;
            foreach (var (name, value) in args) explain.Parameters.AddWithValue(name, value ?? DBNull.Value);
            var plan = new List<string>();
            await using var reader = await explain.ExecuteReaderAsync();
            while (await reader.ReadAsync()) plan.Add(reader.GetString(3));
            result.Add((System.Text.RegularExpressions.Regex.Replace(sql, "\\s+", " "), plan));
        }
        return result;
    }

    private static List<string> PlanOf(List<(string Sql, List<string> Plan)> plans, string from) =>
        plans.Where(x => x.Sql.Contains(from, StringComparison.Ordinal)).SelectMany(x => x.Plan).ToList() is { Count: > 0 } found
            ? found : throw new InvalidOperationException("no statement contains: " + from);

    [Theory]
    [InlineData(UserRole.Manager)]
    [InlineData(UserRole.Doctor)]
    [InlineData(UserRole.Cashier)]
    [InlineData(UserRole.Receptionist)]
    public async Task WidgetStatements_UseIndexes_NoTenantWideScan(UserRole role)
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();
        await SeedAsync(fixture);
        var plans = await PlansAsync(fixture, role);
        Assert.NotEmpty(plans);

        // No statement scans a table (the one allowed SCAN is the already-bounded UNION co-routine "u").
        foreach (var (sql, plan) in plans)
            Assert.False(plan.Any(line => line.StartsWith("SCAN ", StringComparison.Ordinal)
                && !line.StartsWith("SCAN u", StringComparison.Ordinal)), sql);

        if (role != UserRole.Doctor)
        {
            // Day totals and newest-first preview: one (tenant, day) seek; the preview sorts nothing.
            var preview = PlanOf(plans, "ORDER BY \"a\".\"Id\" DESC");
            Assert.Contains(preview, l => l.Contains("IX_ActivityEvents_TenantId_LocalDay_Id", StringComparison.Ordinal)
                && l.Contains("LocalDay=?", StringComparison.Ordinal));
            Assert.False(preview.Any(l => l.Contains("TEMP B-TREE", StringComparison.Ordinal)), string.Join(" | ", preview));
            var totals = PlanOf(plans, "history.visit.completed");
            Assert.Contains(totals, l => l.Contains("IX_ActivityEvents_TenantId_LocalDay_Id", StringComparison.Ordinal));
            var queue = PlanOf(plans, "COUNT(*) FROM \"Appointments\" AS \"a\" LEFT JOIN");
            Assert.Contains(queue, l => l.Contains("IX_Appointments_TenantId_StartUtc_DoctorUserId", StringComparison.Ordinal));
            Assert.Contains(queue, l => l.Contains("IX_ReceptionQueueEntries_TenantId_AppointmentId", StringComparison.Ordinal));
            var stock = PlanOf(plans, "FROM \"Products\" AS \"p\"");
            Assert.Contains(stock, l => l.Contains("SEARCH s USING INDEX IX_StockOnHandSnapshots_TenantId_ProductId_VariantId", StringComparison.Ordinal));
        }
        else
        {
            // Assigned-doctor scope seeks the (tenant, doctor, time) indexes, never the actor/activity feed.
            Assert.DoesNotContain(plans, x => x.Sql.Contains("\"ActivityEvents\"", StringComparison.Ordinal));
            Assert.Contains(PlanOf(plans, "FROM \"Visits\" AS \"v\""), l => l.Contains("IX_Visits_TenantId_DoctorUserId_CompletedUtc", StringComparison.Ordinal));
            Assert.Contains(PlanOf(plans, "FROM \"VaccinationRecords\" AS \"v\" WHERE"), l => l.Contains("IX_VaccinationRecords_TenantId_AdministeredUtc", StringComparison.Ordinal));
            var queue = PlanOf(plans, "COUNT(*) FROM \"Appointments\" AS \"a\" LEFT JOIN");
            Assert.Contains(queue, l => l.Contains("IX_Appointments_TenantId_DoctorId_StartUtc", StringComparison.Ordinal));
            Assert.Contains(PlanOf(plans, "\"v\".\"NextDueUtc\""),
                l => l.Contains("IX_MedicalHistoryEntries_TenantId_SourceEntityId", StringComparison.Ordinal));
        }

        if (role == UserRole.Cashier)
        {
            var ready = PlanOf(plans, "VisitBillingHandoffs");
            Assert.Contains(ready, l => l.Contains("IX_VisitBillingHandoffs_TenantId_Status_CompletedUtc", StringComparison.Ordinal));
            Assert.Contains(ready, l => l.Contains("IX_BoardingBillingHandoffs_TenantId_Status_CompletedUtc", StringComparison.Ordinal));
        }

        // The projection-lag probe seeks per outstanding status; it never scans the succeeded history.
        Assert.Contains(PlanOf(plans, "FROM \"OutboxMessages\""), l => l.Contains("SEARCH o USING COVERING INDEX IX_OutboxMessages_Status_NextAttemptUtc", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ActivityTotals_AreTenantIsolated_AndBoundToTheRequestedDay()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();
        await SeedAsync(fixture); // 300 own rows spread over three days, 300 foreign rows
        await using var db = fixture.CreateContext(fixture.TenantId);
        var access = new DashboardAccess(UserRole.Manager, true, true, true, true, true, true, true, true, true, true);
        var slice = await new DashboardQueries(db).GetSliceAsync(fixture.TenantId, Window, "u", access, default);
        Assert.Equal(100, slice.ActivityTotals!.Visits);                  // own tenant, 20260301 only (i%3==0 && i even → i%6==0)
        Assert.All(slice.Activity, x => Assert.StartsWith("history.", x.TitleKey));
        Assert.Equal(80, slice.Activity.Count);                  // preview stays bounded
    }
}
