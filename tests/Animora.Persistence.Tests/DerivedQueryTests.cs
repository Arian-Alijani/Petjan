using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Animora.Domain.Finance;
using Animora.Domain.Inventory;
using Animora.Domain.Sales;
using Animora.Domain.ValueObjects;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Queries;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Animora.Persistence.Tests;

/// <summary>
/// A10d's derived read models: on-hand is the signed ledger sum, an invoice balance is
/// grand-total − signed payments − signed adjustments, a ledger balance is debit − credit per
/// account, and each is <b>one</b> real SQL command (verified by counting commands, not substrings).
/// </summary>
public sealed class DerivedQueryTests
{
    private static readonly DateTimeOffset Base = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task StockOnHand_IsTheSignedLedgerSum()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();

        await Add(fixture,
            StockMovement.Create(fixture.TenantId, "P1", "V1", MovementKind.Receipt, new Quantity(10m, "EA"),
                Money.Irt(1_000), Base, "U1").Value!,
            StockMovement.Create(fixture.TenantId, "P1", "V1", MovementKind.Sale, new Quantity(-3m, "EA"),
                Money.Irt(1_000), Base, "U1").Value!,
            StockMovement.Create(fixture.TenantId, "P1", "V1", MovementKind.AdjustmentOut, new Quantity(-1.5m, "EA"),
                Money.Irt(1_000), Base, "U1").Value!,
            StockMovement.Create(fixture.TenantId, "P2", "V2", MovementKind.Receipt, new Quantity(4m, "BOX"),
                Money.Irt(2_000), Base, "U1").Value!);

        var (context, counter) = fixture.CreateCountingContext();
        await context.Database.OpenConnectionAsync();
        counter.Reset();

        var onHand = await new StockOnHandQuery(context).GetAsync(["P1", "P2"]);

        Assert.Equal(1, counter.Count); // one grouped SQL aggregate.
        Assert.Equal(5.5m, onHand[("P1", "V1")]);
        Assert.Equal(4m, onHand[("P2", "V2")]);
    }

    [Fact]
    public async Task InvoiceBalance_SubtractsSignedPaymentsAndAdjustments()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();

        var invoice = Invoice.Create(fixture.TenantId, "INV-1", InvoiceKind.DirectSale, Base, "U1",
            ownerId: "OWN1").Value!;
        invoice.AddLine(new InvoiceLine("L1", InvoiceLineKind.Service, null, null, null, "Exam",
            new Quantity(1m, "PCS"), Money.Irt(100_000), 0m, 0m, InvoiceLineSource.Manual, null));
        Assert.True(invoice.Post(fixture.Clock).IsSuccess);

        var receipt = Payment.Create(fixture.TenantId, invoice.Id, PaymentMethod.Card, Money.Irt(40_000),
            Base, "U1").Value!;
        Assert.True(invoice.ApplyPayment(receipt).IsSuccess);
        var creditNote = new InvoiceAdjustment("ADJ-1", AdjustmentKind.CreditNote, Money.Irt(10_000),
            "goodwill", Base, "U1", null, null);
        Assert.True(invoice.AddCreditNote(creditNote).IsSuccess);

        await Add(fixture, invoice, receipt);

        var (context, counter) = fixture.CreateCountingContext();
        await context.Database.OpenConnectionAsync();
        counter.Reset();

        var balances = await new InvoiceBalanceQuery(context).GetAsync([invoice.Id]);

        Assert.Equal(1, counter.Count); // one SQL command.
        var balance = balances[invoice.Id];
        Assert.Equal(100_000L, balance.GrandTotalMinor);
        Assert.Equal(40_000L, balance.PaidMinor);
        Assert.Equal(10_000L, balance.AdjustmentsMinor);
        Assert.Equal(50_000L, balance.DueMinor); // 100_000 - 40_000 - 10_000.
    }

    [Fact]
    public async Task LedgerBalance_IsDebitMinusCreditPerAccount()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();

        await Add(fixture,
            Entry("JE-1", "1405-07", AccountCodes.CashOnHand, AccountCodes.ServiceRevenue, 5_000),
            Entry("JE-2", "1405-07", AccountCodes.CashOnHand, AccountCodes.ServiceRevenue, 3_000));

        var (context, counter) = fixture.CreateCountingContext();
        await context.Database.OpenConnectionAsync();
        counter.Reset();

        var balances = await new LedgerBalanceQuery(context).GetAsync(["1405-07"]);

        Assert.Equal(1, counter.Count); // one grouped SQL.
        var cash = balances.Single(b => b.AccountCode == AccountCodes.CashOnHand);
        Assert.Equal(8_000L, cash.DebitMinor);
        Assert.Equal(0L, cash.CreditMinor);
        Assert.Equal(8_000L, cash.BalanceMinor);

        var revenue = balances.Single(b => b.AccountCode == AccountCodes.ServiceRevenue);
        Assert.Equal(0L, revenue.DebitMinor);
        Assert.Equal(8_000L, revenue.CreditMinor);
        Assert.Equal(-8_000L, revenue.BalanceMinor);
    }

    [Fact]
    public async Task Paging_IsBoundedCountedAndStable()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();

        for (var i = 1; i <= 5; i++)
        {
            await Add(fixture, Invoice.Create(fixture.TenantId, $"INV-{i:D2}", InvoiceKind.DirectSale,
                Base.AddMinutes(i), "U1", ownerId: "OWN1").Value!);
        }

        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        var query = db.Set<Invoice>().OrderBy(i => i.InvoiceNumber);

        var page1 = await query.ToPagedAsync(1, 2, fixture.Options);
        Assert.Equal(5, page1.TotalCount);
        Assert.Equal(2, page1.Items.Count);
        Assert.Equal(1, page1.Page);
        Assert.Equal(2, page1.PageSize);

        // Page size is clamped to the configured ceiling.
        var clamped = await query.ToPagedAsync(1, 9_999, fixture.Options);
        Assert.Equal(Math.Clamp(fixture.Options.MaxPageSize, 1, QueryPaging.HardMaxPageSize), clamped.PageSize);

        // A page past the end is empty but still reports the real total.
        var past = await query.ToPagedAsync(99, 2, fixture.Options);
        Assert.Empty(past.Items);
        Assert.Equal(5, past.TotalCount);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => query.ToPagedAsync(0, 2, fixture.Options));
    }

    [Fact]
    public async Task Paging_WithoutOrdering_Throws()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.MigrateAsync();

        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => db.Set<Invoice>().ToPagedAsync(1, 10, fixture.Options));
    }

    private static JournalEntry Entry(string number, string period, string debitAccount, string creditAccount, long minor) =>
        JournalEntry.Create("T-LOCAL-DEFAULT", number, period, JournalSource.Manual,
            [
                new JournalLine("L1", debitAccount, EntrySide.Debit, Money.Irt(minor), null, null),
                new JournalLine("L2", creditAccount, EntrySide.Credit, Money.Irt(minor), null, null),
            ],
            "memo", "U1", Base).Value!;

    private static async Task Add(LocalStoreTestFixture fixture, params object[] rows)
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        foreach (var row in rows)
        {
            db.Add(row);
        }

        await db.SaveChangesAsync();
    }
}
