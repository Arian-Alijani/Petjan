using Animora.Application.UseCases.Sales;
using Animora.Domain.Inventory;
using Animora.Domain.Results;
using Animora.Domain.Sales;
using Animora.Domain.ValueObjects;
using Xunit;

namespace Animora.Application.Tests;

public sealed class SalesCompensationTests
{
    [Fact]
    public async Task Return_LeavesLineUntouched_CreditsOnce_AndRestocksOnce()
    {
        var invoice = SalesTestData.Posted();
        var originalLine = invoice.Lines[0];
        var originalGrandTotal = invoice.GrandTotal;
        var movements = new StockMovementRepositoryFake();
        var handler = new ReturnInvoiceLineCommandHandler(new SalesInvoiceRepository(invoice),
            movements, new SalesPostingFake(), SalesTestData.Clock);

        var result = await handler.HandleAsync(new ReturnInvoiceLineCommand(invoice.Id, originalLine.LineId, "Customer return"),
            SalesTestData.Context, default);

        Assert.True(result.IsSuccess, result.Detail);
        Assert.Equal(originalGrandTotal, invoice.GrandTotal);
        Assert.Equal(originalLine, invoice.Lines[0]);
        Assert.Equal(originalLine.LineNet, Assert.Single(invoice.Adjustments).Amount);
        Assert.Equal(MovementKind.ReturnIn, Assert.Single(movements.Added).Kind);
        var duplicate = await handler.HandleAsync(new ReturnInvoiceLineCommand(invoice.Id, originalLine.LineId, "Duplicate"),
            SalesTestData.Context with { OperationId = "operation-2" }, default);
        Assert.True(duplicate.IsFailure);
        Assert.Single(invoice.Adjustments);
        Assert.Single(movements.Added);
    }

    [Fact]
    public async Task RefundAbovePaid_IsRejectedWithoutWritingPayment()
    {
        var invoice = SalesTestData.Posted();
        var payments = new SalesPaymentRepository();
        var handler = new RefundInvoiceCommandHandler(new SalesInvoiceRepository(invoice), payments, SalesTestData.Clock);
        var result = await handler.HandleAsync(new RefundInvoiceCommand(invoice.Id, Money.Irt(100), "Refund"),
            SalesTestData.Context, default);
        Assert.Equal(ErrorCodes.AdjustmentExceedsPaid, result.Error);
        Assert.Empty(payments.Added);
        Assert.Empty(invoice.Adjustments);
    }

    [Fact]
    public async Task PaidRefund_AppendsAdjustmentAndNegativePayment_WithoutEditingLines()
    {
        var invoice = SalesTestData.Posted();
        var originalLines = invoice.Lines.ToArray();
        var originalGrandTotal = invoice.GrandTotal;
        var payments = new SalesPaymentRepository();
        var register = new RegisterPaymentCommandHandler(new SalesInvoiceRepository(invoice), payments, SalesTestData.Clock);
        var received = await register.HandleAsync(new RegisterPaymentCommand(invoice.Id, PaymentMethod.Transfer,
            originalGrandTotal), SalesTestData.Context, default);
        Assert.True(received.IsSuccess, received.Detail);
        var refund = new RefundInvoiceCommandHandler(new SalesInvoiceRepository(invoice), payments, SalesTestData.Clock);
        var result = await refund.HandleAsync(new RefundInvoiceCommand(invoice.Id, Money.Irt(500), "Refund",
            PaymentMethod.Transfer), SalesTestData.Context with { OperationId = "refund-2" }, default);

        Assert.True(result.IsSuccess, result.Detail);
        Assert.Equal(InvoiceState.Refunded, invoice.State);
        Assert.Equal(originalLines, invoice.Lines);
        Assert.Equal(originalGrandTotal, invoice.GrandTotal);
        Assert.Single(invoice.Adjustments);
        Assert.Equal(-500L, payments.Added[1].Amount.Minor);
    }

    [Fact]
    public async Task PaidInvoice_CannotBeVoided()
    {
        var invoice = SalesTestData.Posted();
        var payments = new SalesPaymentRepository();
        var register = new RegisterPaymentCommandHandler(new SalesInvoiceRepository(invoice), payments, SalesTestData.Clock);
        Assert.True((await register.HandleAsync(new RegisterPaymentCommand(invoice.Id, PaymentMethod.Transfer,
            invoice.GrandTotal), SalesTestData.Context, default)).IsSuccess);
        var handler = new VoidInvoiceCommandHandler(new SalesInvoiceRepository(invoice), SalesTestData.Clock);
        var result = await handler.HandleAsync(new VoidInvoiceCommand(invoice.Id, "Too late"), SalesTestData.Context, default);
        Assert.Equal(ErrorCodes.InvoiceVoidAfterPayment, result.Error);
        Assert.Equal(InvoiceState.Paid, invoice.State);
    }
}
