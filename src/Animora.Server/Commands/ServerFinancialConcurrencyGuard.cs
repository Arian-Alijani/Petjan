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
        // Numbering/directory locks precede the per-aggregate variant lock in every command.
        if (command is IJournaledCommand journaled)
        {
            if (journaled.CommandTypeName.StartsWith("purchasing.", StringComparison.Ordinal)) resources.Add("purchasing");
            // No clinic-wide "ledger-period" lock is taken any more. It previously queued EVERY financial
            // command (sales.invoice.post / .void / .refund / .line.return, sales.payment.register,
            // cashier.session.close and all finance.*) behind one constant key even though each merely appends
            // one independent, self-balancing journal row — so with many concurrent cashiers the whole posting
            // path ran strictly serially (the measured p95 was lock-wait, not CPU). Every invariant it appeared
            // to protect is already enforced at finer granularity, so dropping it loses no safety:
            //   • Journal numbers  — atomic INSERT … ON CONFLICT DO UPDATE counter (gap-free, per tenant+period
            //                        row lock only); proven by DocumentCounterTests without any advisory lock.
            //   • Lazy period open — atomic (TenantId, PeriodCode) upsert (IPeriodRepository.EnsureAsync); two
            //                        concurrent first-posts into a new month converge instead of racing on an
            //                        un-retried 23505 — the one race that formerly needed this lock.
            //   • Commit ordering  — the deferred TenantFeedState ChangeSequence trigger, held only for the
            //                        commit tail, not the whole transaction.
            //   • Balance          — a per-entry invariant (ServerFinancialEventProcessor), no shared state.
            // Each post still holds its own invoice:{id} / cashier:{user} fence plus the per-aggregate lock
            // below, so concurrent invoices and payments now run in parallel instead of behind one global mutex.
            //
            // finance.period.close (a reserved, not-yet-implemented command) is the one deliberate "freeze the
            // book" operation. When it is built it must fence against concurrent posting through the period's own
            // aggregate lock (AccountingPeriod:{id}, added below) plus the period's optimistic Version token —
            // NOT by reinstating a clinic-wide mutex on the hot path.
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
