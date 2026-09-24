using System;
using Animora.Domain.Results;
using Animora.Domain.Sales;
using Animora.Domain.ValueObjects;
using Xunit;

namespace Animora.Domain.Tests;

/// <summary>
/// A-RULE 002: once an invoice leaves <see cref="InvoiceState.Draft"/>, every economics mutator
/// returns <c>INVOICE_IMMUTABLE</c> — all five of them.
/// </summary>
public sealed class InvoiceImmutabilityTests
{
    private static readonly DateTimeOffset Issued = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void EveryMutator_AfterPost_ReturnsInvoiceImmutable()
    {
        var clock = new FakeClock(Issued);
        var invoice = InvoiceStateMachineTests.Posted(clock);

        AssertImmutable(invoice.AddLine(InvoiceStateMachineTests.Line("L2", 5_000)));
        AssertImmutable(invoice.RemoveLine(invoice.Lines[0].LineId));
        AssertImmutable(invoice.EditLine(InvoiceStateMachineTests.Line(invoice.Lines[0].LineId, 9_999)));
        AssertImmutable(invoice.SetDiscount(DiscountKind.Percent, 10m));
        AssertImmutable(invoice.SetInsurance("INS-1", 30m));

        // The invoice is untouched: same single line, same totals, no discount/insurance applied.
        Assert.Single(invoice.Lines);
        Assert.Equal(Money.ZeroIrt, invoice.DiscountTotal);
        Assert.Equal(Money.ZeroIrt, invoice.InsuranceCoverage);
    }

    [Fact]
    public void Mutators_AreLegalWhileDraft()
    {
        var clock = new FakeClock(Issued);
        var invoice = Invoice.Create("T1", "INV-1", InvoiceKind.DirectSale, clock.UtcNow, "U1", ownerId: "OWN1").Value!;

        Assert.True(invoice.AddLine(InvoiceStateMachineTests.Line("L1", 10_000)).IsSuccess);
        Assert.True(invoice.SetDiscount(DiscountKind.Percent, 10m).IsSuccess);
        Assert.True(invoice.SetInsurance("INS-1", 30m).IsSuccess);
        Assert.Equal(Money.Irt(1_000), invoice.DiscountTotal);
        Assert.Equal(Money.Irt(2_700), invoice.InsuranceCoverage);
    }

    [Fact]
    public void Post_PostedInvoiceAgain_IsRefused()
    {
        var clock = new FakeClock(Issued);
        var invoice = InvoiceStateMachineTests.Posted(clock);

        var result = invoice.Post(clock);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCodes.InvoiceInvalidTransition, result.Error);
    }

    private static void AssertImmutable(Result result)
    {
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCodes.InvoiceImmutable, result.Error);
    }
}
