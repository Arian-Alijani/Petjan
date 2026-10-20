using Animora.Application.Commands;
using Animora.Application.UseCases.Inventory;
using Animora.Application.UseCases.Purchasing;
using Animora.Persistence.PostgreSql.Context;
using Animora.Domain.Sales;
using Microsoft.EntityFrameworkCore;
using Animora.Application.UseCases.Boarding;
using Animora.Application.UseCases.Cashier;
using Animora.Application.UseCases.Sales;
using Animora.Domain.Inventory;
using Animora.Domain.Results;
using Animora.Persistence.PostgreSql.Commands;
using Animora.Persistence.PostgreSql.Repositories;

namespace Animora.Server.Commands;

internal sealed class ServerFinancialConcurrencyGuard(ServerSchedulingConcurrencyGuard scheduling,
    MoneyResourceLock locks, ServerCommandExecutionScope execution, AnimoraServerDbContext db) : IServerCommandConcurrencyGuard
{
    public async Task<Result> PrepareAsync(object command, CancellationToken ct = default)
    {
        if (command is ReceiveGoodsCommand { AllowOverReceipt: true })
            return Result.Fail(ErrorCodes.ValidationFailed, "Over-receipt policy is server-owned.");
        var resources = new List<string>();
        if (command is IJournaledCommand { AggregateTypeName: "Invoice", AggregateIdOrNull: { } invoiceId })
            resources.Add("invoice:" + invoiceId);
        if (command is OpenCashSessionCommand or CloseCashSessionCommand or RecordCashMovementCommand
            or RegisterPaymentCommand or RefundInvoiceCommand or ReturnInvoiceLineCommand)
            resources.Add("cashier:" + execution.UserId);
        // Global numbering/directory and ledger-period resources precede variant locks in every command.
        if (command is IJournaledCommand journaled)
        {
            if (journaled.CommandTypeName.StartsWith("purchasing.", StringComparison.Ordinal)) resources.Add("purchasing");
            if (journaled.CommandTypeName.StartsWith("finance.", StringComparison.Ordinal)
                || journaled.CommandTypeName is "sales.invoice.post" or "sales.invoice.void" or "sales.invoice.refund"
                    or "sales.invoice.line.return" or "sales.payment.register" or "cashier.session.close") resources.Add("ledger-period");
            if (journaled.AggregateIdOrNull is { } id) resources.Add(journaled.AggregateTypeName + ":" + id);
        }
        await locks.AcquireAsync(resources, ct);
        var stock = new List<string>();
        if (command is AdjustStockCommand adjust) stock.Add($"stock:{adjust.ProductId}:{adjust.VariantId}");
        if (command is SetStockCommand set) stock.AddRange(set.Lines.Select(l => $"stock:{set.ProductId}:{l.VariantId}"));
        if (command is ReceiveGoodsCommand receive) stock.AddRange(receive.Lines.Select(l => $"stock:{l.ProductId}:{l.VariantId}"));
        if (command is PostInvoiceCommand post)
        {
            var invoice = await db.Set<Invoice>().AsNoTracking().FirstOrDefaultAsync(i => i.Id == post.InvoiceId, ct);
            // Stay lines were consumed at add-time (AddStayItemCommand) and PostInvoiceCommand skips them, so they
            // take no variant lock here either.
            if (invoice is not null) stock.AddRange(invoice.Lines
                .Where(l => l.ProductId != null && l.VariantId != null && l.Source != InvoiceLineSource.Stay)
                .Select(l => $"stock:{l.ProductId}:{l.VariantId}"));
        }
        // Adding a boarding stay item now moves the warehouse, so it must take the same per-variant stock lock as
        // the other stock-touching commands. The catalog item id for a product is its variant id. (RemoveStayItem
        // is already serialised against concurrent adds by this stay's aggregate lock acquired above.)
        if (command is AddStayItemCommand add)
        {
            var mapped = await (from p in db.Set<Product>().AsNoTracking()
                from v in p.Variants
                where p.Kind != ProductKind.Service && (v.VariantId == add.CatalogItemId || p.Id == add.CatalogItemId)
                select $"stock:{p.Id}:{v.VariantId}").FirstOrDefaultAsync(ct);
            if (mapped is not null) stock.Add(mapped);
        }
        await locks.AcquireAsync(stock, ct);
        return await scheduling.PrepareAsync(command, ct);
    }
}
