using Animora.Application.Abstractions;
using Animora.Application.Dispatch;
using Animora.Application.UseCases.Sales.ReadModels;

namespace Animora.Application.UseCases.Sales;

/// <summary>
/// Registers the C07 Sales/POS use cases: the eight read projections and the sixteen journaled
/// commands (draft editing, posting, payment and the compensating void/refund/return) with their
/// validators. Chained into <c>AddAnimoraApplication</c> from the composition root.
/// </summary>
public static class SalesRegistration
{
    public static ApplicationRegistrationBuilder AddSalesUseCases(this ApplicationRegistrationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder
            // ── Reads ─────────────────────────────────────────────────────────────
            .AddQuery<ReadyForBillingQuery, IReadOnlyList<ReadyForBillingReadModel>, ReadyForBillingQueryHandler>()
            .AddQuery<InvoiceListQuery, PagedResult<InvoiceSummaryReadModel>, InvoiceListQueryHandler>()
            .AddQuery<InvoiceDetailQuery, InvoiceDetailReadModel?, InvoiceDetailQueryHandler>()
            .AddQuery<SalesCatalogQuery, SalesCatalogReadModel, SalesCatalogQueryHandler>()
            .AddQuery<InsurersQuery, IReadOnlyList<InsurerReadModel>, InsurersQueryHandler>()
            .AddQuery<SaleKpiQuery, SaleKpiReadModel, SaleKpiQueryHandler>()
            .AddQuery<SalesHistoryQuery, CashierHistoryPageReadModel, SalesHistoryQueryHandler>()
            .AddQuery<OwnerLookupQuery, IReadOnlyList<OwnerLookupReadModel>, OwnerLookupQueryHandler>()
            // ── Draft editing ─────────────────────────────────────────────────────
            .AddCommand<CreateInvoiceFromVisitCommand, string, CreateInvoiceFromVisitCommandHandler>()
            .AddValidator<CreateInvoiceFromVisitCommand, CreateInvoiceFromVisitCommandValidator>()
            .AddCommand<CreateInvoiceFromStayCommand, string, CreateInvoiceFromStayCommandHandler>()
            .AddValidator<CreateInvoiceFromStayCommand, CreateInvoiceFromStayCommandValidator>()
            .AddCommand<CreateBlankInvoiceCommand, string, CreateBlankInvoiceCommandHandler>()
            .AddValidator<CreateBlankInvoiceCommand, CreateBlankInvoiceCommandValidator>()
            .AddCommand<AddInvoiceLineCommand, Unit, AddInvoiceLineCommandHandler>()
            .AddValidator<AddInvoiceLineCommand, AddInvoiceLineCommandValidator>()
            .AddCommand<AddCustomInvoiceLineCommand, Unit, AddCustomInvoiceLineCommandHandler>()
            .AddValidator<AddCustomInvoiceLineCommand, AddCustomInvoiceLineCommandValidator>()
            .AddCommand<EditInvoiceLineCommand, Unit, EditInvoiceLineCommandHandler>()
            .AddValidator<EditInvoiceLineCommand, EditInvoiceLineCommandValidator>()
            .AddCommand<RemoveInvoiceLineCommand, Unit, RemoveInvoiceLineCommandHandler>()
            .AddValidator<RemoveInvoiceLineCommand, RemoveInvoiceLineCommandValidator>()
            .AddCommand<SetInvoiceDiscountCommand, Unit, SetInvoiceDiscountCommandHandler>()
            .AddValidator<SetInvoiceDiscountCommand, SetInvoiceDiscountCommandValidator>()
            .AddCommand<SetInvoiceInsuranceCommand, Unit, SetInvoiceInsuranceCommandHandler>()
            .AddValidator<SetInvoiceInsuranceCommand, SetInvoiceInsuranceCommandValidator>()
            .AddCommand<AssignInvoiceOwnerCommand, Unit, AssignInvoiceOwnerCommandHandler>()
            .AddValidator<AssignInvoiceOwnerCommand, AssignInvoiceOwnerCommandValidator>()
            .AddCommand<DeleteDraftInvoiceCommand, Unit, DeleteDraftInvoiceCommandHandler>()
            .AddValidator<DeleteDraftInvoiceCommand, DeleteDraftInvoiceCommandValidator>()
            // ── Posting, payment and the compensating commands ────────────────────
            .AddCommand<PostInvoiceCommand, Unit, PostInvoiceCommandHandler>()
            .AddValidator<PostInvoiceCommand, PostInvoiceCommandValidator>()
            .AddCommand<RegisterPaymentCommand, Unit, RegisterPaymentCommandHandler>()
            .AddValidator<RegisterPaymentCommand, RegisterPaymentCommandValidator>()
            .AddCommand<VoidInvoiceCommand, Unit, VoidInvoiceCommandHandler>()
            .AddValidator<VoidInvoiceCommand, VoidInvoiceCommandValidator>()
            .AddCommand<RefundInvoiceCommand, Unit, RefundInvoiceCommandHandler>()
            .AddValidator<RefundInvoiceCommand, RefundInvoiceCommandValidator>()
            .AddCommand<ReturnInvoiceLineCommand, Unit, ReturnInvoiceLineCommandHandler>()
            .AddValidator<ReturnInvoiceLineCommand, ReturnInvoiceLineCommandValidator>();
    }
}
