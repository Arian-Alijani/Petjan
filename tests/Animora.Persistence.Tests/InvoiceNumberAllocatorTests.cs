using System;
using System.Linq;
using System.Threading.Tasks;
using Animora.Persistence.Sqlite.Sales;
using Animora.Persistence.Sqlite.Seed;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Animora.Persistence.Tests;

public sealed class InvoiceNumberAllocatorTests
{
    [Fact]
    public async Task TwoHundredConcurrentAllocations_AreDistinct()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();

        var tasks = Enumerable.Range(0, 200).Select(async _ =>
        {
            await using var db = fixture.CreateContext(fixture.TenantId);
            var allocator = new InvoiceNumberAllocator(db, new PersianMonthPeriodCodeProvider());
            return await allocator.AllocateAsync(fixture.TenantId, fixture.Clock.UtcNow);
        });

        var numbers = await Task.WhenAll(tasks);
        Assert.Equal(200, numbers.Distinct(StringComparer.Ordinal).Count());
        Assert.All(numbers, number => Assert.StartsWith("04-", number, StringComparison.Ordinal));
    }

    [Fact]
    public async Task RolledBackAllocation_IsNotCommitted()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();
        string rolledBack;

        await using (var db = fixture.CreateContext(fixture.TenantId))
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            var allocator = new InvoiceNumberAllocator(db, new PersianMonthPeriodCodeProvider());
            rolledBack = await allocator.AllocateAsync(fixture.TenantId, fixture.Clock.UtcNow);
            await transaction.RollbackAsync();
        }

        await using var verificationDb = fixture.CreateContext(fixture.TenantId);
        var verificationAllocator = new InvoiceNumberAllocator(verificationDb, new PersianMonthPeriodCodeProvider());
        var committed = await verificationAllocator.AllocateAsync(fixture.TenantId, fixture.Clock.UtcNow);

        // Allocation joins the ambient transaction: rollback removes the reservation and exposes no gap.
        Assert.Equal(rolledBack, committed);
    }
}
