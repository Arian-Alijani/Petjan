using Animora.Application.UseCases.Inventory;
using Animora.Application.UseCases.Purchasing;
using Animora.Application.UseCases.Finance;
using Animora.Application.UseCases.Finance.Cheques;
using Animora.Application.UseCases.Cashier;
using Animora.Contracts.Wire.Commands;
using Animora.Server.Handlers;
using Xunit;
namespace Animora.Server.Tests.HandlersS10;
public sealed class MoneyStockIntakeTests
{
    [Fact] public Task Settles_CreateProductCategoryCommand() => SettlesAsync(typeof(CreateProductCategoryCommand));
    [Fact] public Task Settles_RenameProductCategoryCommand() => SettlesAsync(typeof(RenameProductCategoryCommand));
    [Fact] public Task Settles_DeleteProductCategoryCommand() => SettlesAsync(typeof(DeleteProductCategoryCommand));
    [Fact] public Task Settles_CreateProductCommand() => SettlesAsync(typeof(CreateProductCommand));
    [Fact] public Task Settles_UpdateProductInfoCommand() => SettlesAsync(typeof(UpdateProductInfoCommand));
    [Fact] public Task Settles_AddProductVariantCommand() => SettlesAsync(typeof(AddProductVariantCommand));
    [Fact] public Task Settles_DeleteProductCommand() => SettlesAsync(typeof(DeleteProductCommand));
    [Fact] public Task Settles_AdjustStockCommand() => SettlesAsync(typeof(AdjustStockCommand));
    [Fact] public Task Settles_SetStockCommand() => SettlesAsync(typeof(SetStockCommand));
    [Fact] public Task Settles_CreatePurchaseOrderCommand() => SettlesAsync(typeof(CreatePurchaseOrderCommand));
    [Fact] public Task Settles_ReceiveGoodsCommand() => SettlesAsync(typeof(ReceiveGoodsCommand));
    [Fact] public Task Settles_CreateSupplierCommand() => SettlesAsync(typeof(CreateSupplierCommand));
    [Fact] public Task Settles_CancelPurchaseOrderCommand() => SettlesAsync(typeof(CancelPurchaseOrderCommand));
    [Fact] public Task Settles_AddExpenseCommand() => SettlesAsync(typeof(AddExpenseCommand));
    [Fact] public Task Settles_ReverseExpenseCommand() => SettlesAsync(typeof(ReverseExpenseCommand));
    [Fact] public Task Settles_AddRecurringExpenseCommand() => SettlesAsync(typeof(AddRecurringExpenseCommand));
    [Fact] public Task Settles_EditRecurringExpenseCommand() => SettlesAsync(typeof(EditRecurringExpenseCommand));
    [Fact] public Task Settles_ToggleRecurringExpenseCommand() => SettlesAsync(typeof(ToggleRecurringExpenseCommand));
    [Fact] public Task Settles_PayRecurringExpenseForPeriodCommand() => SettlesAsync(typeof(PayRecurringExpenseForPeriodCommand));
    [Fact] public Task Settles_RegisterChequeCommand() => SettlesAsync(typeof(RegisterChequeCommand));
    [Fact] public Task Settles_UpdateChequeMetadataCommand() => SettlesAsync(typeof(UpdateChequeMetadataCommand));
    [Fact] public Task Settles_DeleteChequeDraftCommand() => SettlesAsync(typeof(DeleteChequeDraftCommand));
    [Fact] public Task Settles_TransitionChequeCommand() => SettlesAsync(typeof(TransitionChequeCommand));
    private static async Task SettlesAsync(Type type)
    {
        using var f = new MoneyStockHandlerFixture();
        var service = f.Clinical.Intake.CreateService(f.Catalog, f.Clinical.Dispatcher);
        var envelope = f.Clinical.Envelope(f.Command(type, false));
        var first = await service.SubmitAsync(envelope);
        Assert.Equal(SettlementOutcome.Accepted, first.Value!.Outcome);
        Assert.Equal(first.Value, (await service.SubmitAsync(envelope)).Value);
        Assert.Equal(1, f.Clinical.Dispatcher.Calls);
        using var rejected = new MoneyStockHandlerFixture();
        var failure = await rejected.Clinical.Intake.CreateService(rejected.Catalog, rejected.Clinical.Dispatcher)
            .SubmitAsync(rejected.Clinical.Envelope(rejected.Command(type, true)));
        Assert.Equal(SettlementOutcome.Rejected, failure.Value!.Outcome);
        Assert.NotNull(failure.Value.ErrorCode);
        Assert.NotEqual("HANDLER_NOT_REGISTERED", failure.Value.ErrorCode);
        Assert.Equal(FinancialEffect.NotApplied, failure.Value.FinancialEffect);
    }
}
