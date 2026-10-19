using Animora.Application.Abstractions;
using Animora.Application.Auditing;
using Animora.Application.Commands;
using Animora.Application.Ports;
using Animora.Application.Security;
using Animora.Application.Validation;
using Animora.Contracts.Wire.Commands;
using Animora.Domain.Identity;
using Animora.Domain.Inventory;
using Animora.Domain.Results;
using Animora.Domain.Sales;
using Animora.Domain.Primitives;
using Animora.Domain.Time;
using Animora.Domain.ValueObjects;

namespace Animora.Application.UseCases.Sales;

/// <summary>Issues a draft and consumes stock in the command journal's single transaction.</summary>
[RequiresPermission(PermissionCodes.SalesPost)]
[Auditable(AuditCategory.Financial, CommandTypes.SalesInvoicePost)]
public sealed record PostInvoiceCommand(string InvoiceId) : ICommand<Unit>, IJournaledCommand
{
    public string CommandTypeName => CommandTypes.SalesInvoicePost;
    public string AggregateTypeName => "Invoice";
    public string? AggregateIdOrNull => InvoiceId;
}

public sealed class PostInvoiceCommandValidator : IValidator<PostInvoiceCommand>
{
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(PostInvoiceCommand c, CancellationToken ct) =>
        SalesDraftRules.Check(Rules.Required(c.InvoiceId, nameof(c.InvoiceId)));
}

public sealed class PostInvoiceCommandHandler(IInvoiceRepository invoices, IInvoiceNumberAllocator numbers,
    IStockMovementRepository movements, ISalesPostingData posting, IClock clock, IBoardingRepository boarding)
    : ICommandHandler<PostInvoiceCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(PostInvoiceCommand c, CommandContext context, CancellationToken ct)
    {
        var found = await SalesDraftRules.Find(invoices, c.InvoiceId, ct);
        if (found.IsFailure) return Result<Unit>.Fail(found.Error);
        var invoice = found.Value!;
        if (invoice.State != InvoiceState.Draft) return Result<Unit>.Fail(ErrorCodes.InvoiceImmutable);
        if (invoice.Lines.Count == 0) return Result<Unit>.Fail(ErrorCodes.InvoiceEmpty);
        // A walk-in («مشتری گذری») invoice has no owner record but carries the guest name — it must still be payable.
        if (string.IsNullOrWhiteSpace(invoice.OwnerId) && string.IsNullOrWhiteSpace(invoice.GuestName))
            return Result<Unit>.Fail(ErrorCodes.InvoiceOwnerRequired);
        var source = invoice.VisitId is not null ? "Visit" : invoice.BoardingStayId is not null ? "Boarding" : null;
        var sourceId = invoice.VisitId ?? invoice.BoardingStayId;
        // Captured while the handoff is still Ready, before MarkHandoffInvoicedAsync flips it: tells the
        // stay-finalization step below whether this is the prepaid room invoice or the final one.
        BoardingHandoffKind? kindBeforeMark = null;
        if (source is not null)
        {
            var status = await posting.GetHandoffStatusAsync(source, sourceId!, ct);
            if (status != HandoffPostStatus.Ready) return Result<Unit>.Fail(status == HandoffPostStatus.AlreadyInvoiced
                ? ErrorCodes.HandoffAlreadyInvoiced : ErrorCodes.NotFound);
            if (invoice.BoardingStayId is { } preStayId)
                kindBeforeMark = await posting.GetReadyBoardingHandoffKindAsync(preStayId, ct);
        }

        // Preflight inventory before changing the invoice or allocating a sequence number. Multiple
        // lines for one variant share the same running balance; snapshot changes only on SaveChanges.
        var balances = new Dictionary<(string Product, string Variant), long>();
        var costs = new Dictionary<(string Product, string Variant), Money>();
        var oversold = false;
        foreach (var line in invoice.Lines)
        {
            if (line.ProductId is null || line.VariantId is null) continue;
            var key = (line.ProductId, line.VariantId);
            if (!balances.TryGetValue(key, out var available))
                available = await posting.GetOnHandScaledAsync(key.ProductId, key.VariantId, ct);
            var scaled = decimal.Truncate(line.Quantity.Value * 1000m);
            if (scaled <= 0 || scaled != line.Quantity.Value * 1000m || scaled > long.MaxValue)
                return Result<Unit>.Fail(ErrorCodes.ValidationFailed);
            var remaining = checked(available - (long)scaled);
            if (!posting.AllowNegativeStock && remaining < 0)
                return Result<Unit>.Fail(ErrorCodes.StockWouldGoNegative);
            oversold |= remaining < 0;
            balances[key] = remaining;
            if (!costs.ContainsKey(key))
                costs[key] = await posting.GetLatestReceiptCostAsync(key.ProductId, key.VariantId, ct);
        }

        // The domain is the sole total authority; no submitted total participates in this command.
        invoice.RecalculateTotals();
        // Fail fast on a zero-total invoice before burning an invoice-number sequence on a post the domain
        // (and the ledger) will reject anyway: a zero grand total yields a journal with no lines
        // (JOURNAL_TOO_FEW_LINES). Post() enforces the same invariant; this only answers earlier and cleaner.
        if (invoice.GrandTotal.Minor == 0L) return Result<Unit>.Fail(ErrorCodes.InvoiceNonPositiveTotal);
        var number = await numbers.AllocateAsync(context.TenantId, invoice.IssuedUtc, ct);
        var numbered = invoice.AssignInvoiceNumber(number);
        if (numbered.IsFailure) return Result<Unit>.Fail(numbered.Error, numbered.Detail);
        var posted = invoice.Post(clock);
        if (posted.IsFailure) return Result<Unit>.Fail(posted.Error, posted.Detail);
        // D07: a permissive (offline) store may post below zero, but the authoritative server never does —
        // flag the post as provisional with the code the server will most likely answer, so the invoice
        // and the sync status never present an oversold sale as final before settlement decides.
        if (oversold)
        {
            var flagged = invoice.MarkSettlement(SettlementStatus.LocallyPosted, ErrorCodes.StockWouldGoNegative.Code);
            if (flagged.IsFailure) return Result<Unit>.Fail(flagged.Error, flagged.Detail);
        }

        var movementOrdinal = 0;
        foreach (var line in invoice.Lines)
        {
            if (line.ProductId is null || line.VariantId is null) continue;
            var key = (line.ProductId, line.VariantId);
            var movement = StockMovement.Create(context.TenantId, key.ProductId, key.VariantId,
                MovementKind.Sale, new Quantity(-line.Quantity.Value, line.Quantity.UnitCode), costs[key],
                clock.UtcNow, context.UserId, sourceEntityType: "Invoice", sourceEntityId: invoice.Id,
                operationId: context.OperationId,
                id: IdGenerator.ForOperation(context.OperationId, movementOrdinal++, clock.UtcNow));
            if (movement.IsFailure) return Result<Unit>.Fail(movement.Error, movement.Detail);
            movements.Add(movement.Value!);
        }
        if (source is not null)
        {
            var status = await posting.MarkHandoffInvoicedAsync(source, sourceId!, invoice.Id, ct);
            if (status != HandoffPostStatus.Ready) return Result<Unit>.Fail(status == HandoffPostStatus.AlreadyInvoiced
                ? ErrorCodes.HandoffAlreadyInvoiced : ErrorCodes.NotFound);
        }
        if (invoice.BoardingStayId is { } stayId)
        {
            var stay = await boarding.FindAsync(stayId, ct);
            if (stay is null) return Result<Unit>.Fail(ErrorCodes.StayNotFound);
            // The kind of the handoff just posted decides what posting does to the stay. A prepaid stay's
            // room invoice (Room) is recorded on the stay but leaves it Active and editable so staff keep
            // adding items; the final invoice (Final, or any postpaid invoice) attaches and checks out.
            var handoffKind = kindBeforeMark ?? BoardingHandoffKind.Final;
            if (handoffKind == BoardingHandoffKind.Room)
            {
                var settled = stay.SettlePrepaidRoom(invoice.Id, invoice.GrandTotal);
                if (settled.IsFailure) return Result<Unit>.Fail(settled.Error, settled.Detail);
            }
            else
            {
                // Checkout was requested before billing. Finish it only once the real invoice
                // is issued, in this same transaction; never leave the room occupied indefinitely.
                var attached = stay.AttachInvoice(invoice.Id);
                if (attached.IsFailure) return Result<Unit>.Fail(attached.Error, attached.Detail);
                var checkedOut = stay.CompleteCheckout(clock);
                if (checkedOut.IsFailure) return Result<Unit>.Fail(checkedOut.Error, checkedOut.Detail);
            }
        }
        // Journal and snapshot writes are automatic in the ambient unit-of-work transaction.
        return Result<Unit>.Ok(Unit.Value);
    }
}
