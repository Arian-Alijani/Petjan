using Animora.Application.UseCases.Sales;
using Animora.Contracts.Wire.Commands;
using Animora.Domain.Sales;
using Animora.Domain.ValueObjects;
using Animora.Domain.Results;
using Xunit;
namespace Animora.Server.Tests.HandlersS09;
public sealed class SalesMoneyTests
{
    [Fact] public async Task ServerIgnoresClientTotals_RecomputesFromInvoiceTotals()
    {
        using var f = new SalesCashierHandlerFixture();
        typeof(Invoice).GetProperty(nameof(Invoice.GrandTotal))!.SetValue(f.Invoice, Money.Irt(999));
        var result = await f.Clinical.Intake.CreateService(f.Catalog, f.Clinical.Dispatcher)
            .SubmitAsync(f.Clinical.Envelope(f.Command(typeof(PostInvoiceCommand), false)));
        Assert.Equal(SettlementOutcome.Accepted, result.Value!.Outcome);
        Assert.Equal(100, f.Invoice.GrandTotal.Minor);
    }
    [Fact] public void PostedInvoice_Immutable_CompensatingOnly()
    {
        using var f = new SalesCashierHandlerFixture();
        f.Invoice.Post(f.Clinical.Intake.Clock);
        Assert.Equal(ErrorCodes.InvoiceImmutable, f.Invoice.AddLine(f.Invoice.Lines[0]).Error);
    }
    [Fact] public void Payment_DuplicateOperation_NoDoubleEffect()
    {
        using var f = new SalesCashierHandlerFixture(); f.Invoice.Post(f.Clinical.Intake.Clock);
        var p = Payment.Create("fixture", f.Invoice.Id, PaymentMethod.Transfer, Money.Irt(10), f.Clinical.Intake.Clock.UtcNow, "user", operationId: "same").Value!;
        Assert.True(f.Invoice.ApplyPayment(p).IsSuccess);
        Assert.Equal(ErrorCodes.PaymentDuplicateOperation, f.Invoice.ApplyPayment(p).Error);
        Assert.Equal(10, f.Invoice.PaidTotal.Minor);
    }
    [Fact] public async Task FinancialEffect_AppliedExactlyOnce_OnReplay()
    {
        using var f = new SalesCashierHandlerFixture();
        var envelope = f.Clinical.Envelope(f.Command(typeof(RegisterPaymentCommand), false));
        var service = f.Clinical.Intake.CreateService(f.Catalog, f.Clinical.Dispatcher);
        var first = await service.SubmitAsync(envelope);
        Assert.Equal(FinancialEffect.Applied, first.Value!.FinancialEffect);
        Assert.Equal(first.Value, (await service.SubmitAsync(envelope)).Value);
        Assert.Single(f.Payments.Added);
    }
}
