using System;
using System.Collections.Generic;
using Animora.Domain.Results;
using Animora.Domain.Sales;
using Animora.Domain.ValueObjects;
using Xunit;

namespace Animora.Domain.Tests;

/// <summary>
/// The invoice state machine: exactly the tabled transitions are legal, and an invoice with money on
/// it is refunded rather than voided (<c>INVOICE_VOID_AFTER_PAYMENT</c>).
/// </summary>
public sealed class InvoiceStateMachineTests
{
    private static readonly DateTimeOffset Issued = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    private static readonly HashSet<(InvoiceState From, InvoiceState To)> Allowed =
    [
        (InvoiceState.Draft, InvoiceState.Posted),
        (InvoiceState.Draft, InvoiceState.Void),
        (InvoiceState.Posted, InvoiceState.PartiallyPaid),
        (InvoiceState.Posted, InvoiceState.Paid),
        (InvoiceState.Posted, InvoiceState.Void),
        (InvoiceState.PartiallyPaid, InvoiceState.Paid),
        (InvoiceState.PartiallyPaid, InvoiceState.Refunded),
        (InvoiceState.Paid, InvoiceState.Refunded),
    ];

    [Fact]
    public void AllStatePairs_MatchTheTable()
    {
        var states = Enum.GetValues<InvoiceState>();
        Assert.Equal(36, states.Length * states.Length);

        foreach (var from in states)
        {
            foreach (var to in states)
            {
                Assert.Equal(Allowed.Contains((from, to)), Invoice.IsTransitionAllowed(from, to));
            }
        }
    }

    internal static InvoiceLine Line(string id, long unitPrice, decimal quantity = 1m) =>
        new(id, InvoiceLineKind.Service, null, null, null, "Consultation",
            new Quantity(quantity, "PCS"), Money.Irt(unitPrice), 0m, 0m, InvoiceLineSource.Manual, null);

    internal static Invoice Posted(FakeClock clock, long amount = 100_000, string number = "INV-1")
    {
        var invoice = Invoice.Create("T1", number, InvoiceKind.DirectSale, clock.UtcNow, "U1", ownerId: "OWN1").Value!;
        invoice.AddLine(Line("L1", amount));
        Assert.True(invoice.Post(clock).IsSuccess);
        return invoice;
    }

    [Fact]
    public void Post_WithNoLines_IsRefused()
    {
        var clock = new FakeClock(Issued);
        var invoice = Invoice.Create("T1", "INV-X", InvoiceKind.DirectSale, clock.UtcNow, "U1", ownerId: "OWN1").Value!;

        var result = invoice.Post(clock);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCodes.InvoiceEmpty, result.Error);
    }

    [Fact]
    public void Post_FreezesTheInvoiceAndMovesSettlement()
    {
        var clock = new FakeClock(Issued);
        var invoice = Posted(clock);

        Assert.Equal(InvoiceState.Posted, invoice.State);
        Assert.Equal(SettlementStatus.LocallyPosted, invoice.Settlement);
        Assert.Equal(clock.UtcNow, invoice.PostedUtc);
        Assert.True(invoice.IsImmutable);
    }

    [Fact]
    public void PartialPayment_MovesToPartiallyPaid()
    {
        var clock = new FakeClock(Issued);
        var invoice = Posted(clock, 100_000);
        var payment = Payment.Create("T1", invoice.Id, PaymentMethod.Card, Money.Irt(40_000),
            clock.UtcNow, "U1").Value!;

        Assert.True(invoice.ApplyPayment(payment).IsSuccess);
        Assert.Equal(InvoiceState.PartiallyPaid, invoice.State);
        Assert.Equal(Money.Irt(60_000), invoice.DueTotal);
    }

    [Fact]
    public void FullPayment_MovesToPaid()
    {
        var clock = new FakeClock(Issued);
        var invoice = Posted(clock, 100_000);
        var payment = Payment.Create("T1", invoice.Id, PaymentMethod.Card, Money.Irt(100_000),
            clock.UtcNow, "U1").Value!;

        Assert.True(invoice.ApplyPayment(payment).IsSuccess);
        Assert.Equal(InvoiceState.Paid, invoice.State);
        Assert.Equal(clock.UtcNow, invoice.SettledUtc);
    }

    [Fact]
    public void Void_AfterPartialPayment_IsRefused()
    {
        var clock = new FakeClock(Issued);
        var invoice = Posted(clock, 100_000);
        var payment = Payment.Create("T1", invoice.Id, PaymentMethod.Card, Money.Irt(40_000),
            clock.UtcNow, "U1").Value!;
        invoice.ApplyPayment(payment);

        var result = invoice.Void("mistake", clock);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCodes.InvoiceVoidAfterPayment, result.Error);
        Assert.Equal(InvoiceState.PartiallyPaid, invoice.State);
    }

    [Fact]
    public void Void_BeforeAnyPayment_Succeeds()
    {
        var clock = new FakeClock(Issued);
        var invoice = Posted(clock);

        Assert.True(invoice.Void("duplicate", clock).IsSuccess);
        Assert.Equal(InvoiceState.Void, invoice.State);
    }

    [Fact]
    public void TerminalStates_RefuseFurtherTransitions()
    {
        var clock = new FakeClock(Issued);
        var invoice = Posted(clock);
        invoice.Void("duplicate", clock);

        var payment = Payment.Create("T1", invoice.Id, PaymentMethod.Card, Money.Irt(1), clock.UtcNow, "U1").Value!;
        var result = invoice.ApplyPayment(payment);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCodes.InvoiceInvalidTransition, result.Error);
    }
}
