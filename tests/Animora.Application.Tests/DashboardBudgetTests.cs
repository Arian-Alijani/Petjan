using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Animora.Client.Services.Dashboard;
using Animora.Contracts.Navigation;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Entities;
using Animora.Persistence.Sqlite.Queries;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Animora.Application.Tests;

/// <summary>
/// C10 dashboard budget & role fidelity: composing a snapshot for any role stays within the
/// eight-<c>DbCommand</c> budget (seven slice reads + one version probe), the Manager's "today
/// revenue" equals the finance projection's same-day figure (single source of truth —
/// <c>DailyFinanceFact</c>), and the Doctor snapshot exposes no manager-only rich widget.
/// </summary>
public sealed class DashboardBudgetTests
{
    // The persistence fixture and CommandCounter live in Animora.Persistence.Tests (referenced here).
    private const int Today = 20260301;

    private static async Task SeedRevenueFactAsync(Animora.Persistence.Tests.LocalStoreTestFixture fixture, int localDay, long revenue)
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        var fact = DailyFinanceFact.Create(fixture.TenantId, localDay);
        fact.Apply(revenue: revenue);
        db.Add(fact);
        await db.SaveChangesAsync();
    }

    [Theory]
    [InlineData(UserRole.Manager)]
    [InlineData(UserRole.Doctor)]
    [InlineData(UserRole.Cashier)]
    [InlineData(UserRole.Receptionist)]
    public async Task Snapshot_StaysWithinEightCommandBudget(UserRole role)
    {
        using var fixture = new Animora.Persistence.Tests.LocalStoreTestFixture();
        await fixture.MigrateAsync();
        await SeedRevenueFactAsync(fixture, Today, 4_200_000);

        var (counting, counter) = fixture.CreateCountingContext();
        await using (counting)
        {
            var queries = new DashboardQueries(counting);
            counter.Reset();
            // The composer runs VersionAsync (1) then GetSliceAsync (the role slice) on one connection.
            _ = await queries.VersionAsync(fixture.TenantId, default);
            _ = await queries.GetSliceAsync(fixture.TenantId, Today, "user-1", role, default);
            Assert.True(counter.Count <= 8, $"Role {role} issued {counter.Count} commands (budget: 8).");
        }
    }

    [Fact]
    public async Task ManagerRevenue_EqualsFinanceSameDayFigure()
    {
        const long expected = 7_654_321;
        using var fixture = new Animora.Persistence.Tests.LocalStoreTestFixture();
        await fixture.MigrateAsync();
        await SeedRevenueFactAsync(fixture, Today, expected);

        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        var slice = await new DashboardQueries(db).GetSliceAsync(fixture.TenantId, Today, "mgr", UserRole.Manager, default);

        // The mapper's "today revenue" stat is the last (today) revenue-day value verbatim.
        var snapshot = InvokeMap(UserRole.Manager, slice);
        var todayRevenueStat = snapshot.Stats[0];
        Assert.Equal(expected.ToString("N0", CultureInfo.InvariantCulture), todayRevenueStat.Value);

        // And it comes straight from DailyFinanceFact — the same source the finance screen reads.
        var factRevenue = await db.Set<DailyFinanceFact>().AsNoTracking()
            .Where(x => x.TenantId == fixture.TenantId && x.LocalDay == Today)
            .Select(x => x.RevenueMinor).SingleAsync();
        Assert.Equal(expected, factRevenue);
        Assert.Equal(factRevenue, slice.RevenueDays[^1].RevenueMinor);
    }

    [Fact]
    public async Task DoctorSnapshot_HasNoManagerWidget()
    {
        using var fixture = new Animora.Persistence.Tests.LocalStoreTestFixture();
        await fixture.MigrateAsync();
        await SeedRevenueFactAsync(fixture, Today, 1_000_000);

        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        var slice = await new DashboardQueries(db).GetSliceAsync(fixture.TenantId, Today, "doc", UserRole.Doctor, default);

        var snapshot = InvokeMap(UserRole.Doctor, slice);
        // All three manager rich widgets must be absent for a Doctor.
        Assert.Null(snapshot.RevenuePulse);
        Assert.Null(snapshot.DoctorLoads);
        Assert.Null(snapshot.Species);
    }

    [Fact]
    public async Task ManagerSnapshot_CarriesAllThreeRichWidgets()
    {
        using var fixture = new Animora.Persistence.Tests.LocalStoreTestFixture();
        await fixture.MigrateAsync();
        await SeedRevenueFactAsync(fixture, Today, 1_000_000);

        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        var slice = await new DashboardQueries(db).GetSliceAsync(fixture.TenantId, Today, "mgr", UserRole.Manager, default);

        var snapshot = InvokeMap(UserRole.Manager, slice);
        Assert.NotNull(snapshot.RevenuePulse);
        Assert.NotNull(snapshot.DoctorLoads);
        Assert.NotNull(snapshot.Species);
    }

    // DashboardMapping is internal to Client.Services; Application.Tests has InternalsVisibleTo.
    private static Contracts.Dashboard.DashboardSnapshot InvokeMap(
        UserRole role, Application.UseCases.Dashboard.DashboardSlice slice) =>
        DashboardMapping.Map(role, slice);
}
