using System.Diagnostics;
using Animora.Application.Security;
using Animora.Client.Services.Dashboard;
using Animora.Contracts.Navigation;
using Animora.Domain.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Animora.Application.Tests;

/// <summary>
/// D24: the budget of the production pipeline — <see cref="DashboardQueryService"/> → session → live authorization
/// (membership + role definition reads) → generation probe → dispatcher → queries — counted by an interceptor on
/// every scope/connection the one call opens. Seed/setup is outside the measured window; authorization is never
/// removed to fit a number. Time stamps are recorded as evidence, never asserted (shared hosts are noisy).
/// </summary>
public sealed class DashboardPipelineBudgetTests(ITestOutputHelper output)
{
    private static async Task<(int Count, List<string> Sql, double[] Stamps)> MeasureAsync(
        Func<Task> call, DashboardAuthorizationTests.SqlRecorder sql)
    {
        sql.Commands.Clear();
        var started = Stopwatch.StartNew();
        await call();
        var total = started.Elapsed.TotalMilliseconds;
        return (sql.Commands.Count, [.. sql.Commands], [total]);
    }

    // Miss = authorization + generation probe + slice (the persistence budget of DashboardBudgetTests, which
    // already contains the in-transaction generation read); hit = authorization + generation probe only.
    [Theory]
    [InlineData(StaffRole.Manager, UserRole.Manager)]
    [InlineData(StaffRole.Doctor, UserRole.Doctor)]
    [InlineData(StaffRole.Receptionist, UserRole.Receptionist)]
    [InlineData(StaffRole.Cashier, UserRole.Cashier)]
    public async Task Pipeline_MissAndHit_AreBoundedPerRole_WithoutNPlusOne(StaffRole staff, UserRole layout)
    {
        var sql = new DashboardAuthorizationTests.SqlRecorder();
        await using var host = await DashboardAuthorizationTests.CreateHostAsync(staff, sql);
        var service = new DashboardQueryService(host.Services.GetRequiredService<IServiceScopeFactory>());

        var miss = await MeasureAsync(() => service.GetSnapshotAsync(layout), sql);
        var hit = await MeasureAsync(() => service.GetSnapshotAsync(layout), sql);
        var second = await MeasureAsync(() => service.GetSnapshotAsync(layout), sql);

        output.WriteLine($"{staff}: miss={miss.Count} cmds in {miss.Stamps[0]:F1} ms; hit={hit.Count} cmds in {hit.Stamps[0]:F1} ms");
        foreach (var statement in miss.Sql.Select(x => x.Replace('\n', ' ')))
            output.WriteLine("   " + statement[..Math.Min(110, statement.Length)]);

        // Every hit costs the same fixed authorization + probe work (no growth with repeated calls: no N+1).
        Assert.Equal(hit.Count, second.Count);
        Assert.True(hit.Count < miss.Count, $"hit {hit.Count} must be cheaper than miss {miss.Count}");
        Assert.Contains(hit.Sql, x => x.Contains("\"DashboardGenerations\""));
        Assert.DoesNotContain(hit.Sql, x => x.Contains("\"ActivityEvents\"") || x.Contains("\"Appointments\""));
        Assert.Contains(miss.Sql, x => x.Contains("\"StaffMemberships\"")); // production membership read is in the budget
        // Authorization is a small constant, and the whole miss stays within the documented role bound.
        Assert.InRange(hit.Count, 2, MaxHit(staff));
        Assert.InRange(miss.Count, hit.Count + 1, MaxMiss(staff));
    }

    private static int MaxHit(StaffRole role) => 6;

    // Measured 2026-10-01 after batching the widget permissions (was 52–54 miss / 20–22 hit): 20–24 / 6.
    private static int MaxMiss(StaffRole role) => role switch
    {
        StaffRole.Doctor => 23,
        StaffRole.Cashier => 24,
        StaffRole.Receptionist => 22,
        _ => 26,
    };

    // The batched read must decide exactly like the per-code evaluator, including stored grant/deny overrides.
    [Fact]
    public async Task BatchedPermissions_MatchPerCodeDecisions_ForGrantDenyAndRoleDefaults()
    {
        await using var host = await DashboardAuthorizationTests.CreateHostAsync(StaffRole.Receptionist);
        string[] codes = [PermissionCodes.FinanceRead, PermissionCodes.PaymentsRegister, PermissionCodes.InventoryRead,
            PermissionCodes.SalesRead, PermissionCodes.VisitsRead, PermissionCodes.VaccinationRead,
            PermissionCodes.ChequesRead, PermissionCodes.AppointmentsRead, PermissionCodes.PatientsRead, "not.a.permission"];
        async Task AssertSameAsync()
        {
            using var scope = host.Services.CreateScope();
            var evaluator = scope.ServiceProvider.GetRequiredService<IPermissionEvaluator>();
            var session = host.Cache.Current!;
            var subject = new AuthorizationSubject(Animora.Domain.Identity.Tenant.LocalDefaultId, session.UserId, new HashSet<string>(),
                session.Role!.Value, "device", false);
            var batched = await evaluator.AllowedAsync(codes, subject, default);
            foreach (var code in codes)
                Assert.Equal((await evaluator.EvaluateAsync(code, subject, null, default)).Allowed, batched.Contains(code));
        }
        await AssertSameAsync();
        await DashboardAuthorizationTests.ChangeMemberAsync(host, m => m.GrantExtra(PermissionCodes.FinanceRead));
        await AssertSameAsync();
        await DashboardAuthorizationTests.ChangeMemberAsync(host, m => m.Deny(PermissionCodes.AppointmentsRead));
        await AssertSameAsync();
    }
}
