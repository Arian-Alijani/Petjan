using Animora.Application.Dispatch;
using Animora.Application.UseCases.Sales;
using Animora.Application.UseCases.Cashier;
using Animora.Contracts.Wire.Commands;
using Animora.Server.Commands;

namespace Animora.Server.Handlers;

internal static class ServerHandlerModuleS09
{
    public static void AddHandlers(ApplicationRegistrationBuilder builder) => builder.AddSalesUseCases().AddCashierUseCases()
        .AddCommand<CreateInvoiceFromHandoffCommand, string, CreateInvoiceFromHandoffCommandHandler>();
    public static void AddPayloads(CommandPayloadCatalogBuilder catalog)
    {
        catalog.Add<CreateBlankInvoiceCommand>(CommandTypes.SalesInvoiceCreate, "Invoice");
        catalog.Add<CreateInvoiceFromHandoffCommand>(CommandTypes.SalesInvoiceCreate, "Invoice");
        catalog.Add<AddInvoiceLineCommand>(CommandTypes.SalesInvoiceLineAdd, "Invoice");
        catalog.Add<AddCustomInvoiceLineCommand>(CommandTypes.SalesInvoiceLineAdd, "Invoice");
        catalog.Add<EditInvoiceLineCommand>(CommandTypes.SalesInvoiceLineEdit, "Invoice");
        catalog.Add<RemoveInvoiceLineCommand>(CommandTypes.SalesInvoiceLineRemove, "Invoice");
        catalog.Add<SetInvoiceDiscountCommand>(CommandTypes.SalesInvoiceDiscountSet, "Invoice");
        catalog.Add<SetInvoiceInsuranceCommand>(CommandTypes.SalesInvoiceInsuranceSet, "Invoice");
        catalog.Add<AssignInvoiceOwnerCommand>(CommandTypes.SalesInvoiceOwnerSet, "Invoice");
        catalog.Add<DeleteDraftInvoiceCommand>(CommandTypes.SalesInvoiceDelete, "Invoice");
        catalog.Add<PostInvoiceCommand>(CommandTypes.SalesInvoicePost, "Invoice", FinancialEffect.Applied);
        catalog.Add<RegisterPaymentCommand>(CommandTypes.SalesPaymentRegister, "Invoice", FinancialEffect.Applied);
        catalog.Add<VoidInvoiceCommand>(CommandTypes.SalesInvoiceVoid, "Invoice", FinancialEffect.Applied);
        catalog.Add<RefundInvoiceCommand>(CommandTypes.SalesInvoiceRefund, "Invoice", FinancialEffect.Applied);
        catalog.Add<ReturnInvoiceLineCommand>(CommandTypes.SalesInvoiceLineReturn, "Invoice", FinancialEffect.Applied);
        catalog.Add<CreateInsurerCommand>(CommandTypes.SalesInsurerCreate, "Insurer");
        catalog.Add<UpdateInsurerCommand>(CommandTypes.SalesInsurerUpdate, "Insurer");
        catalog.Add<DeleteInsurerCommand>(CommandTypes.SalesInsurerDelete, "Insurer");
        catalog.Add<CreateServiceRateCommand>(CommandTypes.SalesServiceRateCreate, "Product");
        catalog.Add<CreateBoardingServiceRateCommand>(CommandTypes.SalesServiceRateCreate, "BoardingServiceDef");
        catalog.Add<UpdateServiceRateCommand>(CommandTypes.SalesServiceRateUpdate, "Product");
        catalog.Add<UpdateServiceRateCommand>(CommandTypes.SalesServiceRateUpdate, "BoardingServiceDef");
        catalog.Add<OpenCashSessionCommand>(CommandTypes.CashierSessionOpen, "CashSession");
        catalog.Add<CloseCashSessionCommand>(CommandTypes.CashierSessionClose, "CashSession", FinancialEffect.Applied);
        catalog.Add<RecordCashMovementCommand>(CommandTypes.CashierMovementRecord, "CashSession", FinancialEffect.Applied);
    }
}
