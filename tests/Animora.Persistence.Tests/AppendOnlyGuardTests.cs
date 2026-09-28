using System;
using System.Linq;
using System.Threading.Tasks;
using Animora.Domain.Clinical;
using Animora.Domain.Finance;
using Animora.Domain.Inventory;
using Animora.Domain.Sales;
using Animora.Domain.ValueObjects;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Animora.Persistence.Tests;

/// <summary>
/// A10's append-only guard: an update or delete of any protected type (including its child rows)
/// throws, while a new child row under a mutable owner is allowed.
/// </summary>
public sealed class AppendOnlyGuardTests
{
    private static readonly DateTimeOffset Base = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ProtectedTypes_AreTheExpectedSet()
    {
        var expected = new[]
        {
            typeof(JournalEntry), typeof(JournalLine), typeof(MedicalHistoryEntry),
            typeof(StockMovement), typeof(Payment), typeof(ChequeEvent), typeof(StoreMaintenanceLog),
            typeof(LoginAttemptLog), typeof(AuditEntry), // B05a audit plane: both are append-only.
        };

        Assert.Equal(expected.Length, AppendOnlyGuard.ProtectedTypes.Count);
        foreach (var type in expected)
        {
            Assert.Contains(type, AppendOnlyGuard.ProtectedTypes);
        }
    }

    [Fact]
    public async Task UpdatingAPayment_Throws()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();

        var payment = Payment.Create(fixture.TenantId, "INV-1", PaymentMethod.Card, Money.Irt(1_000),
            Base, "U1").Value!;
        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            db.Add(payment);
            await db.SaveChangesAsync();
        }

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            var tracked = await db.Set<Payment>().IgnoreQueryFilters().SingleAsync();
            db.Entry(tracked).State = EntityState.Modified;

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
            Assert.Contains("Append-only", ex.Message);
            Assert.Contains(nameof(Payment), ex.Message);
        }
    }

    [Fact]
    public async Task DeletingAStockMovement_Throws()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();

        var movement = StockMovement.Create(fixture.TenantId, "P1", "V1", MovementKind.Receipt,
            new Quantity(10m, "EA"), Money.Irt(1_000), Base, "U1").Value!;
        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            db.Add(movement);
            await db.SaveChangesAsync();
        }

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            var tracked = await db.Set<StockMovement>().IgnoreQueryFilters().SingleAsync();
            db.Remove(tracked);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
            Assert.Contains(nameof(StockMovement), ex.Message);
        }
    }

    [Fact]
    public async Task UpdatingAJournalEntryChildRow_Throws()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();

        var entry = JournalEntry.Create(fixture.TenantId, "JE-1", "1405-07", JournalSource.Manual,
            [
                new JournalLine("L1", AccountCodes.CashOnHand, EntrySide.Debit, Money.Irt(1_000), null, null),
                new JournalLine("L2", AccountCodes.ServiceRevenue, EntrySide.Credit, Money.Irt(1_000), null, null),
            ],
            "memo", "U1", Base).Value!;

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            db.Add(entry);
            await db.SaveChangesAsync();
        }

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            // JournalLine is a dependent row: load it through its tracked root, not standalone.
            var root = await db.Set<JournalEntry>().IgnoreQueryFilters().AsTracking()
                .Include(e => e.Lines).SingleAsync();
            db.Entry(root.Lines[0]).State = EntityState.Modified;

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
            Assert.Contains(nameof(JournalLine), ex.Message);
        }
    }

    [Fact]
    public async Task AddingANewChildRowUnderAMutableOwner_IsAllowed()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();

        var cheque = Cheque.Register(fixture.TenantId, "CHQ-1", ChequeDirection.Inbound,
            Money.Irt(1_000_000), Base, Base, ownerId: "OWN1").Value!;

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            db.Add(cheque);
            await db.SaveChangesAsync();
        }

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            var tracked = await db.Set<Cheque>().IgnoreQueryFilters().AsTracking().SingleAsync();
            Assert.True(tracked.MoveToPortfolio("U1", fixture.Clock).IsSuccess);

            await db.SaveChangesAsync(); // the new ChequeEvent row is Added, not Modified.

            var events = await db.Set<ChequeEvent>().IgnoreQueryFilters().CountAsync();
            Assert.Equal(1, events);
        }
    }
}
