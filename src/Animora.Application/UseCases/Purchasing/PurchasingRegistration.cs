using Animora.Application.Abstractions;
using Animora.Application.Dispatch;
using Animora.Application.UseCases.Purchasing.ReadModels;

namespace Animora.Application.UseCases.Purchasing;

/// <summary>
/// Registers the C06 purchasing use cases: the screen snapshot query and the order/receipt commands.
/// Chained into <c>AddAnimoraApplication</c> from the composition root (C06 T11).
/// </summary>
public static class PurchasingRegistration
{
    public static ApplicationRegistrationBuilder AddPurchasingUseCases(this ApplicationRegistrationBuilder builder) => builder
        .AddQuery<PurchasingSnapshotQuery, PurchasingSnapshotReadModel, PurchasingSnapshotQueryHandler>()
        .AddQuery<SupplierDirectoryQuery, IReadOnlyList<SupplierRowReadModel>, SupplierDirectoryQueryHandler>()
        .AddQuery<PurchaseReceiptListQuery, PurchaseReceiptListReadModel, PurchaseReceiptListQueryHandler>()
        .AddCommand<CreateSupplierCommand, string, CreateSupplierCommandHandler>()
        .AddValidator<CreateSupplierCommand, CreateSupplierCommandValidator>()
        .AddCommand<CreatePurchaseOrderCommand, string, CreatePurchaseOrderCommandHandler>()
        .AddValidator<CreatePurchaseOrderCommand, CreatePurchaseOrderCommandValidator>()
        .AddCommand<ReceiveGoodsCommand, string, ReceiveGoodsCommandHandler>()
        .AddValidator<ReceiveGoodsCommand, ReceiveGoodsCommandValidator>()
        .AddCommand<CancelPurchaseOrderCommand, Unit, CancelPurchaseOrderCommandHandler>()
        .AddValidator<CancelPurchaseOrderCommand, CancelPurchaseOrderCommandValidator>();
}
