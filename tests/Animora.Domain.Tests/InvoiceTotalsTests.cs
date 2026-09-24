using System;
using System.Collections.Generic;
using System.Reflection;
using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Domain.Sales;
using Animora.Domain.ValueObjects;
using Xunit;

namespace Animora.Domain.Tests;

/// <summary>
/// The single totals algorithm: invoice discount is pro-rated across the lines by largest remainder
/// before tax, so no minor unit is lost; insurance takes its share of the taxed discounted total; and
/// <see cref="Invoice.Post"/> refuses stored totals that disagree with a recomputation.
/// </summary>
public sealed class InvoiceTotalsTests
{
    private static readonly DateTimeOffset Issued = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    private static InvoiceLine Line(string id, long gross, decimal taxPercent = 0m) =>
        new(id, InvoiceLineKind.Service, null, null, null, "Item",
            new Quantity(1m, "PCS"), Money.Irt(gross), 0m, taxPercent, InvoiceLineSource.Manual, null);

    [Fact]
    public void SevenPercentDiscount_AcrossThreeLines_LosesNoMinorUnit()
    {
        // Gross 1000 + 1001 + 1002 = 3003; 7% -> 210.21 -> 210 (away from zero). With tax = 100%
        // the tax total is exactly (net after line discounts) - (sum of the pro-rated shares), so a
        // single lost minor unit in the allocation would show up here as 2794 instead of 2793.
        var lines = new List<InvoiceLine> { Line("L1", 1_000, 100m), Line("L2", 1_001, 100m), Line("L3", 1_002, 100m) };

        var totals = InvoiceTotals.Compute(lines, DiscountKind.Percent, 7m, 0m, "IRT");

        Assert.Equal(Money.Irt(3_003), totals.Subtotal);
        Assert.Equal(Money.Irt(210), totals.InvoiceDiscount);
        Assert.Equal(Money.Irt(210), totals.DiscountTotal);
        Assert.Equal(Money.Irt(2_793), totals.TaxTotal);
        Assert.Equal(Money.Irt(5_586), totals.GrandTotal);
    }

    [Fact]
    public void SevenPercentDiscount_IsProratedBeforeTax_PerLine()
    {
        // Three equal 10_000 lines, 7% discount (2_100, 700 each), 10% tax on the discounted base.
        var lines = new List<InvoiceLine> { Line("L1", 10_000, 10m), Line("L2", 10_000, 10m), Line("L3", 10_000, 10m) };

        var totals = InvoiceTotals.Compute(lines, DiscountKind.Percent, 7m, 0m, "IRT");

        Assert.Equal(Money.Irt(2_100), totals.InvoiceDiscount);
        Assert.Equal(Money.Irt(2_790), totals.TaxTotal); // 930 tax per line (10_000 - 700).
        Assert.Equal(Money.Irt(30_690), totals.GrandTotal);
    }

    [Fact]
    public void InsuranceCoverage_AtThirtyPercent_TakesItsShareOfTheTaxedTotal()
    {
        var lines = new List<InvoiceLine> { Line("L1", 1_000, 10m), Line("L2", 1_000, 10m), Line("L3", 1_000, 10m) };

        var totals = InvoiceTotals.Compute(lines, DiscountKind.Percent, 0m, 30m, "IRT");

        Assert.Equal(Money.Irt(3_000), totals.Subtotal);
        Assert.Equal(Money.Irt(300), totals.TaxTotal);
        Assert.Equal(Money.Irt(990), totals.InsuranceCoverage); // 30% of 3_300.
        Assert.Equal(Money.Irt(2_310), totals.GrandTotal);
    }

    [Fact]
    public void FixedAmountDiscount_IsTakenAsGiven()
    {
        var lines = new List<InvoiceLine> { Line("L1", 10_000) };

        var totals = InvoiceTotals.Compute(lines, DiscountKind.Amount, 2_500m, 0m, "IRT");

        Assert.Equal(Money.Irt(2_500), totals.InvoiceDiscount);
        Assert.Equal(Money.Irt(7_500), totals.GrandTotal);
    }

    [Fact]
    public void ComputeDue_SubtractsSignedAdjustments()
    {
        var adjustments = new List<InvoiceAdjustment>
        {
            Adjustment(AdjustmentKind.CreditNote, 1_000),
            Adjustment(AdjustmentKind.Refund, -500),
        };

        var due = InvoiceTotals.ComputeDue(Money.Irt(10_000), Money.Irt(3_000), adjustments);

        Assert.Equal(Money.Irt(6_500), due); // 10_000 - 3_000 - (1_000 - 500).
    }

    [Fact]
    public void Post_DetectsAStoredTotalThatDisagreesWithARecomputation()
    {
        var clock = new FakeClock(Issued);
        var invoice = InvoiceStateMachineTests.Posted(clock);

        // Tamper with the stored grand total behind the aggregate's back (as a stale EF row could),
        // then Post must refuse it as INVOICE_TOTAL_MISMATCH.
        var backing = typeof(Invoice).GetField("<GrandTotal>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!;
        backing.SetValue(invoice, Money.Irt(999_999));

        // Posting again is refused: first the state guard (already Posted) or the totals guard fires;
        // use a fresh draft invoice instead so the totals guard is the one exercised.
        var draft = Invoice.Create("T1", "INV-M", InvoiceKind.DirectSale, clock.UtcNow, "U1", ownerId: "OWN1").Value!;
        draft.AddLine(Line("L1", 10_000));
        typeof(Invoice).GetField("<GrandTotal>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(draft, Money.Irt(999_999));

        var result = draft.Post(clock);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCodes.InvoiceTotalMismatch, result.Error);
    }

    private static InvoiceAdjustment Adjustment(AdjustmentKind kind, long amountMinor) =>
        new(IdGenerator.NewId(Issued), kind, Money.Irt(amountMinor), "reason", Issued, "U1", null, null);
}
