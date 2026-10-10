using Animora.Application.UseCases.Sales;
using Animora.Application.UseCases.Cashier;
using Animora.Contracts.Wire.Commands;
using Animora.Server.Handlers;
using Xunit;
namespace Animora.Server.Tests.HandlersS09;
public sealed class SalesCashierIntakeTests
{
    [Fact] public Task Settles_CreateBlankInvoiceCommand() => SettlesAsync(typeof(CreateBlankInvoiceCommand));
    [Fact] public Task Settles_CreateInvoiceFromHandoffCommand() => SettlesAsync(typeof(CreateInvoiceFromHandoffCommand));
    [Fact] public Task Settles_AddInvoiceLineCommand() => SettlesAsync(typeof(AddInvoiceLineCommand));
    [Fact] public Task Settles_AddCustomInvoiceLineCommand() => SettlesAsync(typeof(AddCustomInvoiceLineCommand));
    [Fact] public Task Settles_EditInvoiceLineCommand() => SettlesAsync(typeof(EditInvoiceLineCommand));
    [Fact] public Task Settles_RemoveInvoiceLineCommand() => SettlesAsync(typeof(RemoveInvoiceLineCommand));
    [Fact] public Task Settles_SetInvoiceDiscountCommand() => SettlesAsync(typeof(SetInvoiceDiscountCommand));
    [Fact] public Task Settles_SetInvoiceInsuranceCommand() => SettlesAsync(typeof(SetInvoiceInsuranceCommand));
    [Fact] public Task Settles_AssignInvoiceOwnerCommand() => SettlesAsync(typeof(AssignInvoiceOwnerCommand));
    [Fact] public Task Settles_DeleteDraftInvoiceCommand() => SettlesAsync(typeof(DeleteDraftInvoiceCommand));
    [Fact] public Task Settles_PostInvoiceCommand() => SettlesAsync(typeof(PostInvoiceCommand));
    [Fact] public Task Settles_RegisterPaymentCommand() => SettlesAsync(typeof(RegisterPaymentCommand));
    [Fact] public Task Settles_VoidInvoiceCommand() => SettlesAsync(typeof(VoidInvoiceCommand));
    [Fact] public Task Settles_RefundInvoiceCommand() => SettlesAsync(typeof(RefundInvoiceCommand));
    [Fact] public Task Settles_ReturnInvoiceLineCommand() => SettlesAsync(typeof(ReturnInvoiceLineCommand));
    [Fact] public Task Settles_CreateInsurerCommand() => SettlesAsync(typeof(CreateInsurerCommand));
    [Fact] public Task Settles_UpdateInsurerCommand() => SettlesAsync(typeof(UpdateInsurerCommand));
    [Fact] public Task Settles_DeleteInsurerCommand() => SettlesAsync(typeof(DeleteInsurerCommand));
    [Fact] public Task Settles_CreateServiceRateCommand() => SettlesAsync(typeof(CreateServiceRateCommand));
    [Fact] public Task Settles_CreateBoardingServiceRateCommand() => SettlesAsync(typeof(CreateBoardingServiceRateCommand));
    [Fact] public Task Settles_UpdateServiceRateCommand() => SettlesAsync(typeof(UpdateServiceRateCommand));
    [Fact] public Task Settles_OpenCashSessionCommand() => SettlesAsync(typeof(OpenCashSessionCommand));
    [Fact] public Task Settles_CloseCashSessionCommand() => SettlesAsync(typeof(CloseCashSessionCommand));
    [Fact] public Task Settles_RecordCashMovementCommand() => SettlesAsync(typeof(RecordCashMovementCommand));
    [Fact] public Task Settles_CreateInvoiceFromVisitCommand() => SettlesAsync(typeof(CreateInvoiceFromVisitCommand));
    [Fact] public Task Settles_CreateInvoiceFromStayCommand() => SettlesAsync(typeof(CreateInvoiceFromStayCommand));
    private static async Task SettlesAsync(Type type)
    {
        using var f = new SalesCashierHandlerFixture();
        var service = f.Clinical.Intake.CreateService(f.Catalog, f.Clinical.Dispatcher);
        var envelope = f.Clinical.Envelope(f.Command(type, false));
        var first = await service.SubmitAsync(envelope);
        Assert.Equal(SettlementOutcome.Accepted, first.Value!.Outcome);
        Assert.Equal(first.Value, (await service.SubmitAsync(envelope)).Value);
        Assert.Equal(1, f.Clinical.Dispatcher.Calls);
        using var rejected = new SalesCashierHandlerFixture();
        var failure = await rejected.Clinical.Intake.CreateService(rejected.Catalog, rejected.Clinical.Dispatcher)
            .SubmitAsync(rejected.Clinical.Envelope(rejected.Command(type, true)));
        Assert.True(failure.IsSuccess, $"{failure.Error.Code}: {failure.Detail}");
        Assert.Equal(SettlementOutcome.Rejected, failure.Value!.Outcome);
        Assert.NotNull(failure.Value.ErrorCode);
        Assert.NotEqual("HANDLER_NOT_REGISTERED", failure.Value.ErrorCode);
        Assert.Equal(FinancialEffect.NotApplied, failure.Value.FinancialEffect);
    }
}
