using Animora.Application.Dispatch;
using Animora.Application.UseCases.Inventory;
using Animora.Application.UseCases.Purchasing;
using Animora.Application.UseCases.Finance;
using Animora.Application.UseCases.Finance.Cheques;
using Animora.Contracts.Wire.Commands;
using Animora.Server.Commands;
namespace Animora.Server.Handlers;
internal static class ServerHandlerModuleS10
{
    public static void AddHandlers(ApplicationRegistrationBuilder builder) =>
        builder.AddInventoryUseCases().AddPurchasingUseCases().AddFinanceUseCases().AddChequeUseCases();
    public static void AddPayloads(CommandPayloadCatalogBuilder catalog)
    {
        catalog.Add<CreateProductCategoryCommand>(CommandTypes.InventoryCategoryCreate, "ProductCategory");
        catalog.Add<RenameProductCategoryCommand>(CommandTypes.InventoryCategoryRename, "ProductCategory");
        catalog.Add<DeleteProductCategoryCommand>(CommandTypes.InventoryCategoryDelete, "ProductCategory");
        catalog.Add<CreateProductCommand>(CommandTypes.InventoryProductCreate, "Product");
        catalog.Add<UpdateProductInfoCommand>(CommandTypes.InventoryProductUpdate, "Product");
        catalog.Add<AddProductVariantCommand>(CommandTypes.InventoryVariantAdd, "Product");
        catalog.Add<DeleteProductCommand>(CommandTypes.InventoryProductDelete, "Product");
        catalog.Add<AdjustStockCommand>(CommandTypes.InventoryStockAdjust, "Product", FinancialEffect.Applied);
        catalog.Add<SetStockCommand>(CommandTypes.InventoryStockSet, "Product", FinancialEffect.Applied);
        catalog.Add<CreatePurchaseOrderCommand>(CommandTypes.PurchasingOrderCreate, "PurchaseOrder");
        catalog.Add<ReceiveGoodsCommand>(CommandTypes.PurchasingReceiptRecord, "GoodsReceipt", FinancialEffect.Applied);
        catalog.Add<CreateSupplierCommand>(CommandTypes.PurchasingSupplierCreate, "Supplier");
        catalog.Add<CancelPurchaseOrderCommand>(CommandTypes.PurchasingOrderCancel, "PurchaseOrder");
        catalog.Add<AddExpenseCommand>(CommandTypes.FinanceExpensePost, "Expense", FinancialEffect.Applied);
        catalog.Add<ReverseExpenseCommand>(CommandTypes.FinanceExpenseReverse, "Expense", FinancialEffect.Applied);
        catalog.Add<AddRecurringExpenseCommand>(CommandTypes.FinanceRecurringAdd, "RecurringExpense");
        catalog.Add<EditRecurringExpenseCommand>(CommandTypes.FinanceRecurringEdit, "RecurringExpense");
        catalog.Add<ToggleRecurringExpenseCommand>(CommandTypes.FinanceRecurringToggle, "RecurringExpense");
        catalog.Add<PayRecurringExpenseForPeriodCommand>(CommandTypes.FinanceRecurringPay, "RecurringExpense", FinancialEffect.Applied);
        catalog.Add<RegisterChequeCommand>(CommandTypes.FinanceChequeCreate, "Cheque");
        catalog.Add<UpdateChequeMetadataCommand>(CommandTypes.FinanceChequeUpdate, "Cheque");
        catalog.Add<DeleteChequeDraftCommand>(CommandTypes.FinanceChequeDelete, "Cheque");
        catalog.Add<TransitionChequeCommand>(CommandTypes.FinanceChequeTransition, "Cheque", FinancialEffect.Applied);
    }
}
