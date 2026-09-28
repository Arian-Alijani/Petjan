using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Animora.Domain.Inventory;
using Animora.Domain.ValueObjects;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Converters;
using Animora.Persistence.Sqlite.Entities;
using Animora.Persistence.Sqlite.Maintenance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Animora.Persistence.Tests;

/// <summary>
/// C06 T12: the maintained <see cref="StockOnHandSnapshot"/> is a "cache with a proof" — it must equal
/// a fresh recomputation over the append-only ledger, the domain must keep <b>no</b> stored on-hand on
/// <c>Product</c>/<c>ProductVariant</c> (on-hand is derived, R-B23), and
/// <see cref="ILocalStoreMaintenance.RebuildStockSnapshotsAsync"/> must repair a corrupted row.
/// </summary>
public sealed class StockSnapshotIntegrityTests
{
    private static readonly DateTimeOffset Base = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Snapshot_EqualsLedgerRecomputation_AfterManyMovements()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();

        // A small key space so the 500 movements collide onto the same (product, variant) rows repeatedly.
        var keys = new[]
        {
            ("P1", "V1", "EA"), ("P1", "V2", "EA"), ("P2", "V1", "BOX"),
            ("P3", "V1", "EA"), ("P3", "V2", "EA"),
        };
        var expected = new Dictionary<(string, string), long>();
        var random = new Random(20260928); // Deterministic — CONVENTIONS §9 forbids wall-clock/nondeterminism.

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            for (var i = 0; i < 500; i++)
            {
                var (product, variant, unit) = keys[random.Next(keys.Length)];
                // Keep the running total non-negative so every kind's sign rule is satisfiable.
                var current = expected.GetValueOrDefault((product, variant));
                var inbound = current <= 0 || random.Next(2) == 0;
                var magnitude = random.Next(1, 20); // 1..19 whole units
                var (kind, signed) = inbound
                    ? (MovementKind.Receipt, magnitude)
                    : (MovementKind.Sale, -Math.Min(magnitude, (int)(current / 1000L)));
                if (signed == 0) { inbound = true; signed = magnitude; kind = MovementKind.Receipt; }

                var quantity = new Quantity(signed, unit);
                var movement = StockMovement.Create(
                    fixture.TenantId, product, variant, kind, quantity, Money.Irt(1_000),
                    Base.AddMinutes(i), "U1").Value!;
                db.Add(movement);
                expected[(product, variant)] = current + FixedScaleQuantityConverter.ToScaled(signed);
            }
            // The updater runs inside SaveChangesAsync (same transaction as the movements).
            await db.SaveChangesAsync();
        }

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();

            // 1) The maintained snapshot equals the expected running totals.
            var snapshots = await db.Set<StockOnHandSnapshot>().AsNoTracking()
                .ToDictionaryAsync(s => (s.ProductId, s.VariantId), s => s.OnHandScaled);
            Assert.Equal(expected.Count, snapshots.Count);
            foreach (var (key, total) in expected)
            {
                Assert.Equal(total, snapshots[key]);
            }

            // 2) The maintained snapshot equals a live recomputation straight from the ledger.
            var recomputed = await db.Set<StockMovement>().AsNoTracking()
                .GroupBy(m => new { m.ProductId, m.VariantId })
                .Select(g => new { g.Key.ProductId, g.Key.VariantId, Sum = g.Sum(m => m.Quantity.Value) })
                .ToListAsync();
            foreach (var row in recomputed)
            {
                Assert.Equal(
                    FixedScaleQuantityConverter.ToScaled(row.Sum),
                    snapshots[(row.ProductId, row.VariantId)]);
            }
        }
    }

    [Fact]
    public async Task EfModel_HasNoStoredOnHandOnProductOrVariant()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();

        // On-hand is derived from the ledger, never a stored absolute on the catalogue aggregate (R-B23).
        AssertNoStockLikeProperty(db.Model.FindEntityType(typeof(Product))!);
        var variant = db.Model.FindEntityType(typeof(ProductVariant))
            ?? db.Model.FindEntityType(typeof(Product))!.GetNavigations()
                .Select(n => n.TargetEntityType)
                .FirstOrDefault(t => t.ClrType == typeof(ProductVariant));
        Assert.NotNull(variant);
        AssertNoStockLikeProperty(variant!);
    }

    [Fact]
    public async Task RebuildStockSnapshots_RepairsACorruptedRow()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            db.Add(StockMovement.Create(fixture.TenantId, "P1", "V1", MovementKind.Receipt,
                new Quantity(10m, "EA"), Money.Irt(1_000), Base, "U1").Value!);
            db.Add(StockMovement.Create(fixture.TenantId, "P1", "V1", MovementKind.Sale,
                new Quantity(-4m, "EA"), Money.Irt(1_000), Base.AddMinutes(1), "U1").Value!);
            await db.SaveChangesAsync();
        }

        // Deliberately corrupt the maintained row so it no longer matches the ledger.
        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            var row = await db.Set<StockOnHandSnapshot>().SingleAsync();
            Assert.Equal(6_000L, row.OnHandScaled); // 10 − 4 = 6, scaled ×1000.
            row.Reset(999_999L, Base, "bogus");
            await db.SaveChangesAsync();
        }

        // Rebuild recomputes every row from the ledger and returns the number written.
        using (var scope = fixture.CreateScope())
        {
            var maintenance = scope.ServiceProvider.GetRequiredService<ILocalStoreMaintenance>();
            var rebuilt = await maintenance.RebuildStockSnapshotsAsync(default);
            Assert.True(rebuilt.IsSuccess, rebuilt.Detail);
            Assert.Equal(1, rebuilt.Value);
        }

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            var row = await db.Set<StockOnHandSnapshot>().AsNoTracking().SingleAsync();
            Assert.Equal(6_000L, row.OnHandScaled); // Repaired back to the true ledger sum.
        }
    }

    private static void AssertNoStockLikeProperty(IEntityType entity)
    {
        foreach (var property in entity.GetProperties())
        {
            var name = property.Name;
            Assert.False(
                name.Equals("Stock", StringComparison.OrdinalIgnoreCase)
                || name.Equals("OnHand", StringComparison.OrdinalIgnoreCase)
                || name.Equals("Quantity", StringComparison.OrdinalIgnoreCase)
                || name.Equals("OnHandScaled", StringComparison.OrdinalIgnoreCase),
                $"'{entity.ClrType.Name}' must not store an on-hand property but has '{name}'.");
        }
    }
}
