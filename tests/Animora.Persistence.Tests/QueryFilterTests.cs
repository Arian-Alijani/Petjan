using System;
using System.Linq;
using System.Threading.Tasks;
using Animora.Domain.Sales;
using Animora.Domain.ValueObjects;
using Animora.Persistence.Sqlite.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Animora.Persistence.Tests;

/// <summary>
/// The two named query filters: <c>Tenant</c> and <c>SoftDelete</c>. Lifting only
/// <c>SoftDelete</c> reveals deleted rows but still hides another tenant, and the tenant write guard
/// refuses a row stamped with a foreign tenant.
/// </summary>
public sealed class QueryFilterTests
{
    private static readonly DateTimeOffset Base = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    private static Invoice NewInvoice(string tenant, string number, bool deleted = false)
    {
        var invoice = Invoice.Create(tenant, number, InvoiceKind.DirectSale, Base, "U1", ownerId: "OWN1").Value!;
        invoice.AddLine(new InvoiceLine("L1", InvoiceLineKind.Service, null, null, null, "Exam",
            new Quantity(1m, "PCS"), Money.Irt(1_000), 0m, 0m, InvoiceLineSource.Manual, null));
        if (deleted)
        {
            invoice.Deleted = true;
        }

        return invoice;
    }

    [Fact]
    public async Task BothFilters_HideTheRightRows()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            db.Add(NewInvoice(fixture.TenantId, "INV-LIVE"));
            db.Add(NewInvoice(fixture.TenantId, "INV-DELETED", deleted: true));
            await db.SaveChangesAsync();
        }

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            var visible = await db.Set<Invoice>().Select(i => i.InvoiceNumber).ToListAsync();
            Assert.Contains("INV-LIVE", visible);
            Assert.DoesNotContain("INV-DELETED", visible); // SoftDelete filter.
        }
    }

    [Fact]
    public async Task IgnoringSoftDeleteOnly_RevealsDeletedRowsButStillHidesAnotherTenant()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            db.Add(NewInvoice(fixture.TenantId, "INV-LIVE"));
            db.Add(NewInvoice(fixture.TenantId, "INV-DELETED", deleted: true));
            await db.SaveChangesAsync();
        }

        // The foreign row is written in its own tenant context, so the tenant write guard passes.
        using (var foreign = fixture.CreateContext("T-OTHER"))
        {
            foreign.Add(NewInvoice("T-OTHER", "INV-FOREIGN"));
            await foreign.SaveChangesAsync();
        }

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            var visible = await db.Set<Invoice>()
                .IgnoreQueryFilters(["SoftDelete"])
                .Select(i => i.InvoiceNumber)
                .ToListAsync();

            Assert.Contains("INV-LIVE", visible);
            Assert.Contains("INV-DELETED", visible); // revealed.
            Assert.DoesNotContain("INV-FOREIGN", visible); // the Tenant filter still holds.
        }
    }

    [Fact]
    public async Task WritingAForeignTenantRow_Throws()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();

        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        db.Add(NewInvoice("T-OTHER", "INV-FOREIGN"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        Assert.Contains("another tenant", ex.Message);
    }

    [Fact]
    public async Task BlankTenantRow_IsStampedWithTheCurrentTenant()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();

        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        var invoice = NewInvoice(fixture.TenantId, "INV-1");
        typeof(Invoice).GetProperty(nameof(Invoice.TenantId))!.SetValue(invoice, string.Empty);
        db.Add(invoice);
        await db.SaveChangesAsync();

        Assert.Equal(fixture.TenantId, invoice.TenantId);
        Assert.Equal(fixture.Clock.UtcNow, invoice.UpdatedAt);
    }
}
