using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Animora.Domain.Finance;
using Animora.Domain.Inventory;
using Animora.Domain.Sales;
using Animora.Domain.ValueObjects;
using Animora.Persistence.Sqlite.Context;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Animora.Persistence.Tests;

/// <summary>
/// A10 business mappings: migration <c>002</c> creates the business tables and indexes, aggregate
/// child rows live in real relational tables (owned-as-rows) with a composite key and cascade, and
/// Money/Quantity map to exact <c>INTEGER</c>+<c>TEXT</c> columns — never a JSON blob.
/// </summary>
public sealed class BusinessMappingTests
{
    private static readonly DateTimeOffset Base = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    private static readonly string[] BusinessTables =
    [
        "AccountingPeriods", "CashMovements", "CashSessions", "ChequeEvents", "Cheques", "Expenses",
        "GoodsReceiptLines", "GoodsReceipts", "InvoiceAdjustments", "InvoiceLines", "Invoices",
        "JournalEntries", "JournalLines", "LedgerAccounts", "Payments", "ProductCategories",
        "ProductVariants", "Products", "PurchaseOrderLines", "PurchaseOrders", "SeedMarkers",
        "StockMovements", "Suppliers", "UnitOfMeasures",
    ];

    private static readonly string[] BusinessIndexes =
    [
        "IX_Invoices_TenantId_Number",
        "IX_JournalEntries_TenantId_EntryNumber",
        "IX_JournalLines_AccountCode",
        "IX_StockMovements_TenantId_ProductId_VariantId_OccurredUtc",
        "IX_SeedMarkers_SeedKey",
        "IX_Products_TenantId_Sku",
        "IX_LedgerAccounts_TenantId_Code",
        "IX_AccountingPeriods_TenantId_PeriodCode",
    ];

    [Fact]
    public async Task BusinessTablesAndIndexesExist()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();

        var tables = await Names(fixture, "table");
        foreach (var table in BusinessTables)
        {
            Assert.Contains(table, tables);
        }

        var indexes = await Names(fixture, "index");
        foreach (var index in BusinessIndexes)
        {
            Assert.Contains(index, indexes);
        }
    }

    [Fact]
    public async Task JournalLines_AreARealRelationalTableWithCompositeKey()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();

        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        var columns = await Columns(db, "JournalLines");

        Assert.Contains("OwnerId", columns.Keys);       // FK to the aggregate root.
        Assert.Contains("LineId", columns.Keys);         // child key.
        Assert.Contains("AmountMinor", columns.Keys);    // Money complex prop.
        Assert.Contains("AmountCurrency", columns.Keys);
        Assert.DoesNotContain(columns.Keys, k => k.Contains("Json", StringComparison.OrdinalIgnoreCase));

        var primaryKey = await PrimaryKeyColumns(db, "JournalLines");
        Assert.Contains("OwnerId", primaryKey);
        Assert.Contains("LineId", primaryKey);
    }

    [Fact]
    public async Task Invoice_ChildRowsRoundTripThroughRelationalTables()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();

        var invoice = Invoice.Create(fixture.TenantId, "INV-1", InvoiceKind.DirectSale, Base, "U1",
            ownerId: "OWN1").Value!;
        invoice.AddLine(new InvoiceLine("L1", InvoiceLineKind.Service, null, null, null, "Exam",
            new Quantity(1m, "PCS"), Money.Irt(12_500), 0m, 0m, InvoiceLineSource.Manual, null));
        invoice.AddLine(new InvoiceLine("L2", InvoiceLineKind.Product, "CAT-1", "P1", "V1", "Drug",
            new Quantity(2m, "PCS"), Money.Irt(5_000), 0m, 0m, InvoiceLineSource.Catalog, null));
        Assert.True(invoice.Post(fixture.Clock).IsSuccess);
        var creditNote = new InvoiceAdjustment("ADJ-1", AdjustmentKind.CreditNote, Money.Irt(1_000),
            "goodwill", Base, "U1", null, null);
        Assert.True(invoice.AddCreditNote(creditNote).IsSuccess);

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            db.Add(invoice);
            await db.SaveChangesAsync();
        }

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            var loaded = await db.Set<Invoice>().Include(i => i.Lines).Include(i => i.Adjustments).SingleAsync();
            Assert.Equal(2, loaded.Lines.Count);
            Assert.Single(loaded.Adjustments);
            Assert.Equal(Money.Irt(12_500), loaded.Lines.Single(l => l.LineId == "L1").UnitPrice);
        }
    }

    [Fact]
    public async Task SummableQuantity_UsesIntegerScaledColumn()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();

        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();

        var movementColumns = await Columns(db, "StockMovements");
        Assert.Equal("INTEGER", movementColumns["QuantityScaled"]);
        Assert.Equal("TEXT", movementColumns["QuantityUnit"]);

        var lineColumns = await Columns(db, "InvoiceLines");
        Assert.Equal("INTEGER", lineColumns["QuantityScaled"]);
    }

    [Fact]
    public async Task StockMovement_RoundTripsSignedFixedScaleQuantity()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();

        var movement = StockMovement.Create(fixture.TenantId, "P1", "V1", MovementKind.Sale,
            new Quantity(-2.5m, "ML"), Money.Irt(1_000), Base, "U1").Value!;

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            db.Add(movement);
            await db.SaveChangesAsync();
        }

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            var loaded = await db.Set<StockMovement>().SingleAsync();
            Assert.Equal(-2.5m, loaded.Quantity.Value);
            Assert.Equal("ML", loaded.Quantity.UnitCode);
        }
    }

    private static async Task<IReadOnlyList<string>> Names(LocalStoreTestFixture fixture, string type)
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        var connection = (SqliteConnection)db.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT name FROM sqlite_master WHERE type='{type}';";
        var rows = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(reader.GetString(0));
        }

        await connection.CloseAsync();
        return rows;
    }

    private static async Task<Dictionary<string, string>> Columns(AnimoraLocalDbContext db, string table)
    {
        var connection = (SqliteConnection)db.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info(\"{table}\");";
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result[reader.GetString(1)] = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
        }

        await connection.CloseAsync();
        return result;
    }

    private static async Task<IReadOnlyList<string>> PrimaryKeyColumns(AnimoraLocalDbContext db, string table)
    {
        var connection = (SqliteConnection)db.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info(\"{table}\");";
        var keys = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (reader.GetInt64(5) > 0)
            {
                keys.Add(reader.GetString(1));
            }
        }

        await connection.CloseAsync();
        return keys;
    }
}
