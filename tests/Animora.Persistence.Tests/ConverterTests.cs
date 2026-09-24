using System;
using System.Linq;
using System.Threading.Tasks;
using Animora.Domain.Boarding;
using Animora.Domain.Inventory;
using Animora.Domain.Sales;
using Animora.Domain.ValueObjects;
using Animora.Persistence.Sqlite.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Animora.Persistence.Tests;

/// <summary>
/// A09 converters: a <see cref="DateTimeOffset"/> with a non-zero offset round-trips as the same UTC
/// instant, <see cref="Money"/> round-trips through <c>&lt;Prop&gt;Minor</c>/<c>&lt;Prop&gt;Currency</c>,
/// and enums persist as <c>int</c>.
/// </summary>
public sealed class ConverterTests
{
    private static readonly DateTimeOffset Base = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task DateTimeOffset_RoundTripsAsTheSameUtcInstant()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();

        // +03:30 offset — stored as UTC ticks, read back as the same instant.
        var withOffset = new DateTimeOffset(2026, 3, 1, 12, 30, 0, TimeSpan.FromMinutes(210));
        var stay = BoardingStay.Reserve(fixture.TenantId, "PET1", "OWN1", "R-01", withOffset,
            withOffset.AddDays(2), Money.Irt(500_000)).Value!;

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            db.Add(stay);
            await db.SaveChangesAsync();
        }

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            var loaded = await db.Set<BoardingStay>().SingleAsync();
            Assert.Equal(withOffset.ToUniversalTime(), loaded.CheckInUtc);
            Assert.Equal(TimeSpan.Zero, loaded.CheckInUtc.Offset);
            Assert.Equal(withOffset.UtcDateTime, loaded.CheckInUtc.UtcDateTime);
        }
    }

    [Fact]
    public async Task Money_RoundTripsThroughMinorAndCurrencyColumns()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();

        var invoice = Invoice.Create(fixture.TenantId, "INV-1", InvoiceKind.DirectSale, Base, "U1",
            ownerId: "OWN1").Value!;
        invoice.AddLine(new InvoiceLine("L1", InvoiceLineKind.Service, null, null, null, "Exam",
            new Quantity(1m, "PCS"), Money.Irt(12_500), 0m, 0m, InvoiceLineSource.Manual, null));

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            db.Add(invoice);
            await db.SaveChangesAsync();
        }

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            var loaded = await db.Set<Invoice>().Include(i => i.Lines).SingleAsync();
            Assert.Equal(Money.Irt(12_500), loaded.Lines[0].UnitPrice);
            Assert.Equal("IRT", loaded.Lines[0].UnitPrice.Currency);
            Assert.Equal(12_500L, loaded.Lines[0].UnitPrice.Minor);
        }

        // The stored shape is INTEGER minor + TEXT currency, never a REAL/decimal money column.
        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            var columns = await ColumnTypes(db, "InvoiceLines");
            Assert.Equal("INTEGER", columns["UnitPriceMinor"]);
            Assert.Equal("TEXT", columns["UnitPriceCurrency"]);
        }
    }

    [Fact]
    public async Task Enum_PersistsAsInt()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();

        var product = Product.Create(fixture.TenantId, "MED-1", "Amoxicillin", "MED",
            ProductKind.Medicine, "EA", Money.Irt(1_000), Money.Irt(2_000),
            [new ProductVariant("V1", "SKU-1", ProductVariant.DefaultAttributeName, "10", Money.Irt(1_000),
                Money.Irt(2_000), Quantity.Zero("EA"), null, true)]).Value!;

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            db.Add(product);
            await db.SaveChangesAsync();
        }

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            var loaded = await db.Set<Product>().SingleAsync();
            Assert.Equal(ProductKind.Medicine, loaded.Kind);
            var columns = await ColumnTypes(db, "Products");
            Assert.Equal("INTEGER", columns["Kind"]);
        }
    }

    private static async Task<System.Collections.Generic.Dictionary<string, string>> ColumnTypes(
        AnimoraLocalDbContext db, string table)
    {
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info(\"{table}\");";
        var result = new System.Collections.Generic.Dictionary<string, string>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result[reader.GetString(1)] = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
        }

        await connection.CloseAsync();
        return result;
    }
}
