using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Entities;
using Animora.Persistence.Sqlite.Maintenance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Animora.Persistence.Tests;

/// <summary>Proves the three finance facts are repairable caches, not the accounting source of truth.</summary>
public sealed class FinanceProjectionIntegrityTests
{
    [Fact]
    public async Task SeededProjections_MatchIndependentRebuild_AndRepairCorruption()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.InitializeAsync(); // Deterministic C05–C08 seed, including invoices and movements.
        var original = await SnapshotAsync(fixture);
        Assert.NotEmpty(original.Daily);
        Assert.NotEmpty(original.Products);
        Assert.NotEmpty(original.Owners);

        using (var scope = fixture.CreateScope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<ILocalStoreMaintenance>()
                .RebuildFinanceProjectionsAsync(default);
            Assert.True(result.IsSuccess, result.Detail);
        }
        var rebuilt = await SnapshotAsync(fixture);
        Assert.Equal(original.Daily, rebuilt.Daily);
        Assert.Equal(original.Products, rebuilt.Products);
        Assert.Equal(original.Owners, rebuilt.Owners);

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            var row = await db.Set<DailyFinanceFact>().OrderBy(f => f.LocalDay).FirstAsync();
            row.Apply(revenue: 999);
            await db.SaveChangesAsync();
        }
        Assert.NotEqual(rebuilt.Daily, (await SnapshotAsync(fixture)).Daily);
        using (var scope = fixture.CreateScope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<ILocalStoreMaintenance>()
                .RebuildFinanceProjectionsAsync(default);
            Assert.True(result.IsSuccess, result.Detail);
        }
        var repaired = await SnapshotAsync(fixture);
        Assert.Equal(rebuilt.Daily, repaired.Daily);
        Assert.Equal(rebuilt.Products, repaired.Products);
        Assert.Equal(rebuilt.Owners, repaired.Owners);
    }

    private static async Task<(string[] Daily, string[] Products, string[] Owners)> SnapshotAsync(LocalStoreTestFixture fixture)
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        // Compare every economic field, not generated row ids, which legitimately change on rebuild.
        var daily = await db.Set<DailyFinanceFact>().AsNoTracking().OrderBy(f => f.LocalDay)
            .Select(f => new { f.LocalDay, f.RevenueMinor, f.CogsMinor, f.ExpenseMinor,
                f.DiscountMinor, f.InsuranceMinor, f.RefundMinor, f.InvoiceCount, f.PaidInvoiceCount })
            .ToListAsync();
        var products = await db.Set<ProductSalesFact>().AsNoTracking()
            .OrderBy(f => f.LocalDay).ThenBy(f => f.ProductId).ThenBy(f => f.VariantId)
            .Select(f => new { f.LocalDay, f.ProductId, f.VariantId, f.QuantitySold, f.RevenueMinor, f.CogsMinor })
            .ToListAsync();
        var owners = await db.Set<OwnerFinancialSnapshot>().AsNoTracking().OrderBy(f => f.OwnerId)
            .Select(f => new { f.OwnerId, f.TotalSpendMinor, f.OpenBalanceMinor, f.LastInvoiceUtc, f.InvoiceCount })
            .ToListAsync();
        return (daily.Select(x => x.ToString()!).ToArray(), products.Select(x => x.ToString()!).ToArray(),
            owners.Select(x => x.ToString()!).ToArray());
    }
}
