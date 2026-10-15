using System;
using System.Linq;
using System.Threading.Tasks;
using Animora.Application.Activity;
using Animora.Application.UseCases.History;
using Animora.Contracts.History;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Entities;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Animora.Persistence.Tests;

/// <summary>
/// D13: the unified-history income total is ACCRUAL, like the canonical daily finance projection
/// (<c>FinanceProjectionRebuilder</c> sums posted invoices). A registered payment is the COLLECTION of a sale that
/// its own <c>history.invoice.posted</c> row already counted, so counting it again reported double income — a
/// 100-unit sale plus its 100-unit payment read as 200, and a 40-unit partial payment read as 140.
/// </summary>
public sealed class ActivityFeedIncomeTests
{
    [Fact]
    public async Task PostedSale_PlusItsFullPayment_ReportsTheSaleOnce()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();
        await SeedAsync(fixture,
            (ActivityEventKeys.InvoicePosted, 100, 1),
            (ActivityEventKeys.PaymentRegistered, 100, 1));

        var page = await PageAsync(fixture);

        Assert.Equal(100, page.IncomeTotal);
        Assert.Equal(0, page.ExpenseTotal);
        Assert.Equal(100, Assert.Single(page.DayNets).Value);
    }

    [Fact]
    public async Task PostedSale_PlusAPartialPayment_StillReportsTheSaleOnce()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();
        await SeedAsync(fixture,
            (ActivityEventKeys.InvoicePosted, 100, 1),
            (ActivityEventKeys.PaymentRegistered, 40, 1));

        var page = await PageAsync(fixture);

        Assert.Equal(100, page.IncomeTotal);
    }

    [Fact]
    public async Task RowsKeepTheirDisplayAmountAndSign()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();
        await SeedAsync(fixture, (ActivityEventKeys.PaymentRegistered, 100, 1));

        var page = await PageAsync(fixture);

        // Excluded from the aggregate, but never blanked on the row itself (the operator must see what was paid).
        var row = Assert.Single(page.Items);
        Assert.Equal(100, row.AmountMinor);
        Assert.Equal(1, row.AmountSign);
        Assert.Equal(0, page.IncomeTotal);
    }

    [Fact]
    public async Task PostedExpense_AndRefund_StillCountAsOutflow()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();
        await SeedAsync(fixture,
            (ActivityEventKeys.InvoicePosted, 100, 1),
            (ActivityEventKeys.ExpensePosted, 30, -1),
            (ActivityEventKeys.InvoiceRefunded, 20, -1));

        var page = await PageAsync(fixture);

        Assert.Equal(100, page.IncomeTotal);
        Assert.Equal(50, page.ExpenseTotal);
    }

    [Fact]
    public async Task DayNet_AndIncome_AreIndependentOfTheVisiblePage()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();
        await SeedAsync(fixture, (ActivityEventKeys.InvoicePosted, 100, 1),
            (ActivityEventKeys.PaymentRegistered, 40, 1), (ActivityEventKeys.ExpensePosted, 20, -1));
        using var scope = fixture.CreateScope();
        var queries = scope.ServiceProvider.GetRequiredService<IActivityFeedQueries>();
        for (var pageNumber = 1; pageNumber <= 3; pageNumber++)
        {
            var page = await queries.PageAsync(fixture.TenantId,
                new HistoryFilter("", "", [], "", HistorySort.Newest, pageNumber, 1), default);
            Assert.Single(page.Items);
            Assert.Equal(100, page.IncomeTotal);
            Assert.Equal(20, page.ExpenseTotal);
            Assert.Equal(80, Assert.Single(page.DayNets).Value);
        }
    }

    private static async Task<ActivityPage> PageAsync(LocalStoreTestFixture fixture)
    {
        using var scope = fixture.CreateScope();
        var queries = scope.ServiceProvider.GetRequiredService<IActivityFeedQueries>();
        return await queries.PageAsync(fixture.TenantId,
            new HistoryFilter("", "", [], "", HistorySort.Newest, 1, 50), default);
    }

    private static async Task SeedAsync(LocalStoreTestFixture fixture,
        params (string Title, long Amount, int Sign)[] rows)
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        var index = 0;
        foreach (var (title, amount, sign) in rows)
        {
            db.Add(ActivityEvent.Create(fixture.TenantId, fixture.Clock.UtcNow, 20260301,
                HistoryEventKind.Sale.ToString(), title, "{}", "Invoice", "E" + index,
                amount, sign, HistoryEventStatus.Done.ToString(), "U1", "User", null));
            index++;
        }
        await db.SaveChangesAsync();
    }
}
