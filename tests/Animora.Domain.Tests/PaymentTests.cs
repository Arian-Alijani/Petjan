using System;
using System.Reflection;
using Animora.Domain.Results;
using Animora.Domain.Sales;
using Animora.Domain.ValueObjects;
using Xunit;

namespace Animora.Domain.Tests;

/// <summary>
/// Payments are append-only and idempotent: an over-payment is refused, a replayed
/// <c>OperationId</c> has no second effect, a refund is a negative compensating entry, and the
/// per-method reference rule holds.
/// </summary>
public sealed class PaymentTests
{
    private static readonly DateTimeOffset Issued = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void OverPayment_IsRefused()
    {
        var clock = new FakeClock(Issued);
        var invoice = InvoiceStateMachineTests.Posted(clock, 100_000);
        var payment = Payment.Create("T1", invoice.Id, PaymentMethod.Card, Money.Irt(100_001),
            clock.UtcNow, "U1").Value!;

        var result = invoice.ApplyPayment(payment);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCodes.PaymentExceedsDue, result.Error);
        Assert.Equal(Money.ZeroIrt, invoice.PaidTotal);
    }

    [Fact]
    public void DuplicateOperationId_IsRefusedAndLeavesPaidTotalUnchanged()
    {
        var clock = new FakeClock(Issued);
        var invoice = InvoiceStateMachineTests.Posted(clock, 100_000);
        var first = Payment.Create("T1", invoice.Id, PaymentMethod.Card, Money.Irt(40_000),
            clock.UtcNow, "U1", operationId: "OP-1").Value!;
        var replay = Payment.Create("T1", invoice.Id, PaymentMethod.Card, Money.Irt(40_000),
            clock.UtcNow, "U1", operationId: "OP-1").Value!;

        Assert.True(invoice.ApplyPayment(first).IsSuccess);
        var result = invoice.ApplyPayment(replay);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCodes.PaymentDuplicateOperation, result.Error);
        Assert.Equal(Money.Irt(40_000), invoice.PaidTotal);
        Assert.Equal(InvoiceState.PartiallyPaid, invoice.State);
    }

    [Fact]
    public void Reversal_IsANegativeCompensatingEntry()
    {
        var clock = new FakeClock(Issued);
        var invoice = InvoiceStateMachineTests.Posted(clock, 100_000);
        var receipt = Payment.Create("T1", invoice.Id, PaymentMethod.Card, Money.Irt(40_000),
            clock.UtcNow, "U1").Value!;
        invoice.ApplyPayment(receipt);
        Assert.Equal(InvoiceState.PartiallyPaid, invoice.State);

        var reversal = Payment.Create("T1", invoice.Id, PaymentMethod.Card, Money.Irt(-40_000),
            clock.UtcNow, "U1", reversesPaymentId: receipt.Id).Value!;
        Assert.True(reversal.IsRefund);
        Assert.True(reversal.IsReversal);

        // A negative amount is applied without the due check: the matching adjustment authorized it.
        Assert.True(invoice.ApplyPayment(reversal).IsSuccess);
        Assert.Equal(Money.ZeroIrt, invoice.PaidTotal);
    }

    [Fact]
    public void CashPayment_RequiresItsCashSession()
    {
        var result = Payment.Create("T1", "INV-1", PaymentMethod.Cash, Money.Irt(1_000),
            Issued, "U1");

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCodes.PaymentMethodReferenceRequired, result.Error);

        Assert.True(Payment.Create("T1", "INV-1", PaymentMethod.Cash, Money.Irt(1_000),
            Issued, "U1", cashSessionId: "CS-1").IsSuccess);
    }

    [Fact]
    public void ChequePayment_RequiresItsCheque()
    {
        var result = Payment.Create("T1", "INV-1", PaymentMethod.Cheque, Money.Irt(1_000),
            Issued, "U1");

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCodes.PaymentMethodReferenceRequired, result.Error);

        Assert.True(Payment.Create("T1", "INV-1", PaymentMethod.Cheque, Money.Irt(1_000),
            Issued, "U1", chequeId: "CHQ-1").IsSuccess);
    }

    [Fact]
    public void ZeroAmount_IsNeverAPayment()
    {
        var result = Payment.Create("T1", "INV-1", PaymentMethod.Card, Money.ZeroIrt, Issued, "U1");

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCodes.ValidationFailed, result.Error);
    }

    [Fact]
    public void PaymentAndAdjustment_ExposeNoPublicSetter()
    {
        AssertNoPublicSetter(typeof(Payment));
        AssertNoPublicSetter(typeof(InvoiceAdjustment));
    }

    private static void AssertNoPublicSetter(Type type)
    {
        foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            // The inherited SyncableEntity transport properties (Id/TenantId/UpdatedAt/Version/Deleted)
            // are the store's bookkeeping, not the payment's economics; the payment's own properties
            // must expose no setter at all.
            if (property.DeclaringType != typeof(Payment) && property.DeclaringType != typeof(InvoiceAdjustment)) continue;
            Assert.Null(property.GetSetMethod(nonPublic: false));
        }
    }
}
