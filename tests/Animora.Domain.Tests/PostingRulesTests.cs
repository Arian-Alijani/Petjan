using System;
using System.Collections.Generic;
using Animora.Domain.Primitives;
using Animora.Domain.Cashier;
using Animora.Domain.Events;
using Animora.Domain.Finance;
using Animora.Domain.Inventory;
using Animora.Domain.Purchasing;
using Animora.Domain.Results;
using Animora.Domain.Sales;
using Animora.Domain.Time;
using Animora.Domain.ValueObjects;
using Xunit;

namespace Animora.Domain.Tests;

/// <summary>
/// The single event → journal translation table: for every rule the produced entry is balanced,
/// posts only to postable accounts of the default chart, and carries the source entity + the
/// command's <c>OperationId</c>.
/// </summary>
public sealed class PostingRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly FakeClock Clock = new(Now);
    private static readonly PostingContext Context = new("JE-1", "1405-07", "U1", "OP-1");

    private static string Id() => IdGenerator.NewId(Now);

    private static void AssertWellFormed(JournalEntry entry, string sourceEntityId)
    {
        Assert.True(entry.Lines.Count >= 2);
        Assert.Equal(sourceEntityId, entry.SourceEntityId);
        Assert.Equal("OP-1", entry.OperationId);

        var debit = 0L;
        var credit = 0L;
        foreach (var line in entry.Lines)
        {
            Assert.True(ChartOfAccounts.IsPostable(line.AccountCode), $"Account '{line.AccountCode}' is not postable.");
            Assert.True(line.Amount.Minor > 0L);
            if (line.IsDebit)
            {
                debit += line.Amount.Minor;
            }
            else
            {
                credit += line.Amount.Minor;
            }
        }

        Assert.Equal(debit, credit);
    }

    private static Invoice PostedInvoice()
    {
        var invoice = Invoice.Create("T1", "INV-1", InvoiceKind.Visit, Now, "U1", ownerId: "OWN1").Value!;
        invoice.AddLine(new InvoiceLine(Id(), InvoiceLineKind.Service, null, null, null, "Exam",
            new Quantity(1m, "PCS"), Money.Irt(200_000), 0m, 10m, InvoiceLineSource.Visit, null));
        invoice.AddLine(new InvoiceLine(Id(), InvoiceLineKind.Product, null, "P1", "V1", "Drug",
            new Quantity(2m, "PCS"), Money.Irt(50_000), 0m, 10m, InvoiceLineSource.Catalog, null));
        invoice.AddLine(new InvoiceLine(Id(), InvoiceLineKind.Boarding, null, null, null, "Night",
            new Quantity(1m, "PCS"), Money.Irt(300_000), 0m, 0m, InvoiceLineSource.Stay, null));
        invoice.SetDiscount(DiscountKind.Percent, 5m);
        invoice.SetInsurance("INS-1", 20m);
        Assert.True(invoice.Post(Clock).IsSuccess);
        return invoice;
    }

    [Fact]
    public void ForInvoicePosted_IsBalancedAndSplitsRevenue()
    {
        var invoice = PostedInvoice();
        var e = new InvoicePostedEvent(Id(), "T1", Now, invoice.Id, invoice.OwnerId,
            invoice.GrandTotal.Minor, invoice.Currency, null, null);

        var result = PostingRules.ForInvoicePosted(e, invoice, Context, Clock);

        Assert.True(result.IsSuccess);
        AssertWellFormed(result.Value!, invoice.Id);
    }

    [Theory]
    [InlineData(PaymentMethod.Cash)]
    [InlineData(PaymentMethod.Card)]
    [InlineData(PaymentMethod.Transfer)]
    [InlineData(PaymentMethod.Cheque)]
    [InlineData(PaymentMethod.Insurance)]
    [InlineData(PaymentMethod.Wallet)]
    public void ForPaymentRegistered_IsBalancedForEveryMethod(PaymentMethod method)
    {
        var e = new PaymentRegisteredEvent(Id(), "T1", Now, "PAY-1", "INV-1", 100_000, "IRT", method);

        var result = PostingRules.ForPaymentRegistered(e, Context, Clock);

        Assert.True(result.IsSuccess);
        AssertWellFormed(result.Value!, "PAY-1");
    }

    [Fact]
    public void ForPaymentRegistered_RefusesCreditAndRefunds()
    {
        var credit = PostingRules.ForPaymentRegistered(
            new PaymentRegisteredEvent(Id(), "T1", Now, "PAY-1", "INV-1", 100_000, "IRT", PaymentMethod.Credit),
            Context, Clock);
        Assert.True(credit.IsFailure);

        var refund = PostingRules.ForPaymentRegistered(
            new PaymentRegisteredEvent(Id(), "T1", Now, "PAY-1", "INV-1", -100_000, "IRT", PaymentMethod.Card),
            Context, Clock);
        Assert.True(refund.IsFailure);
    }

    [Fact]
    public void ForInvoiceRefunded_IsBalanced()
    {
        var e = new InvoiceRefundedEvent(Id(), "T1", Now, "INV-1", "ADJ-1", 100_000, "IRT");

        var result = PostingRules.ForInvoiceRefunded(e, PaymentMethod.Cash, Context, Clock);

        Assert.True(result.IsSuccess);
        AssertWellFormed(result.Value!, "INV-1");
    }

    [Fact]
    public void ForInvoiceVoided_MirrorsThePostingEntry()
    {
        var invoice = PostedInvoice();
        var posting = PostingRules.ForInvoicePosted(
            new InvoicePostedEvent(Id(), "T1", Now, invoice.Id, invoice.OwnerId,
                invoice.GrandTotal.Minor, invoice.Currency, null, null),
            invoice, Context, Clock).Value!;

        var result = PostingRules.ForInvoiceVoided(
            new InvoiceVoidedEvent(Id(), "T1", Now, invoice.Id, "duplicate"), posting, Context, Clock);

        Assert.True(result.IsSuccess);
        var reversal = result.Value!;
        Assert.True(reversal.IsReversal);
        Assert.Equal(posting.Id, reversal.ReversesEntryId);
        AssertWellFormed(reversal, invoice.Id);
    }

    [Fact]
    public void ForGoodsReceiptPosted_IsBalanced()
    {
        var receipt = GoodsReceipt.Create("T1", "GR-1", "SUP1", Now, "U1").Value!;
        receipt.AddLine(new GoodsReceiptLine(Id(), "P1", "V1", new Quantity(10m, "EA"),
            Money.Irt(1_000), null, null));
        var e = new GoodsReceiptPostedEvent(Id(), "T1", Now, receipt.Id, "SUP1", 1);

        var result = PostingRules.ForGoodsReceiptPosted(e, receipt, "IRT", Context, Clock);

        Assert.True(result.IsSuccess);
        AssertWellFormed(result.Value!, receipt.Id);
    }

    [Theory]
    [InlineData(MovementKind.Sale)]
    [InlineData(MovementKind.Consumption)]
    [InlineData(MovementKind.Loss)]
    [InlineData(MovementKind.AdjustmentOut)]
    public void ForStockMovementRecorded_IsBalancedForEveryLedgerKind(MovementKind kind)
    {
        var e = new StockMovementRecordedEvent(Id(), "T1", Now, "MOV-1", "P1", "V1", kind, -5m);

        var result = PostingRules.ForStockMovementRecorded(e, Money.Irt(7_500), Context, Clock);

        Assert.True(result.IsSuccess);
        AssertWellFormed(result.Value!, "MOV-1");
    }

    [Fact]
    public void ForStockMovementRecorded_RefusesKindsWithNoLedgerEffect()
    {
        var result = PostingRules.ForStockMovementRecorded(
            new StockMovementRecordedEvent(Id(), "T1", Now, "MOV-1", "P1", "V1", MovementKind.Receipt, 5m),
            Money.Irt(1_000), Context, Clock);

        Assert.True(result.IsFailure);
    }

    [Theory]
    [InlineData(ChequeDirection.Inbound)]
    [InlineData(ChequeDirection.Outbound)]
    public void ForChequeCleared_IsBalanced(ChequeDirection direction)
    {
        var e = new ChequeClearedEvent(Id(), "T1", Now, "CHQ-1", direction, 100_000, "IRT");

        var result = PostingRules.ForChequeCleared(e, Context, Clock);

        Assert.True(result.IsSuccess);
        AssertWellFormed(result.Value!, "CHQ-1");
    }

    [Theory]
    [InlineData(ChequeDirection.Inbound)]
    [InlineData(ChequeDirection.Outbound)]
    public void ForChequeBounced_IsBalanced(ChequeDirection direction)
    {
        var e = new ChequeBouncedEvent(Id(), "T1", Now, "CHQ-1", direction, 100_000, "IRT", "insufficient funds");

        var result = PostingRules.ForChequeBounced(e, Context, Clock);

        Assert.True(result.IsSuccess);
        AssertWellFormed(result.Value!, "CHQ-1");
    }

    [Theory]
    [InlineData(-50_000)] // shortage
    [InlineData(50_000)]  // overage
    public void ForCashSessionClosed_IsBalanced(long variance)
    {
        var e = new CashSessionClosedEvent(Id(), "T1", Now, "CS-1", variance, "IRT");

        var result = PostingRules.ForCashSessionClosed(e, Context, Clock);

        Assert.True(result.IsSuccess);
        AssertWellFormed(result.Value!, "CS-1");
    }

    [Fact]
    public void ForCashSessionClosed_RefusesAZeroVariance()
    {
        var result = PostingRules.ForCashSessionClosed(
            new CashSessionClosedEvent(Id(), "T1", Now, "CS-1", 0L, "IRT"), Context, Clock);

        Assert.True(result.IsFailure);
    }

    [Theory]
    [InlineData(ExpenseCategories.Salary, PaymentMethod.Cash)]
    [InlineData(ExpenseCategories.Rent, PaymentMethod.Transfer)]
    [InlineData(ExpenseCategories.Supplies, PaymentMethod.Cheque)]
    [InlineData(ExpenseCategories.Other, null)]
    public void ForExpensePosted_IsBalanced(string category, PaymentMethod? settled)
    {
        var e = new ExpensePostedEvent(Id(), "T1", Now, "EXP-1", category, 100_000, "IRT", settled);

        var result = PostingRules.ForExpensePosted(e, Context, Clock);

        Assert.True(result.IsSuccess);
        AssertWellFormed(result.Value!, "EXP-1");
    }

    [Fact]
    public void ForExpensePosted_SalaryPostsTo6100_EverythingElseTo6000()
    {
        var salary = PostingRules.ForExpensePosted(
            new ExpensePostedEvent(Id(), "T1", Now, "EXP-1", ExpenseCategories.Salary, 100_000, "IRT", null),
            Context, Clock).Value!;
        Assert.Contains(salary.Lines, l => l.AccountCode == AccountCodes.SalaryExpense && l.IsDebit);

        var rent = PostingRules.ForExpensePosted(
            new ExpensePostedEvent(Id(), "T1", Now, "EXP-2", ExpenseCategories.Rent, 100_000, "IRT", null),
            Context, Clock).Value!;
        Assert.Contains(rent.Lines, l => l.AccountCode == AccountCodes.OperatingExpense && l.IsDebit);
    }
}
