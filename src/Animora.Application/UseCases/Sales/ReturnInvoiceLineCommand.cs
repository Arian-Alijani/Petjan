using Animora.Application.Abstractions;
using Animora.Application.Auditing;
using Animora.Application.Commands;
using Animora.Application.Ports;
using Animora.Application.Security;
using Animora.Application.Validation;
using Animora.Contracts.Wire.Commands;
using Animora.Domain.Identity;
using Animora.Domain.Inventory;
using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Domain.Sales;
using Animora.Domain.Time;
using Animora.Domain.ValueObjects;

namespace Animora.Application.UseCases.Sales;

[RequiresPermission(PermissionCodes.SalesVoid)]
[Auditable(AuditCategory.Financial, CommandTypes.SalesInvoiceLineReturn)]
public sealed record ReturnInvoiceLineCommand(string InvoiceId, string LineId, string Reason)
    : ICommand<Unit>, IJournaledCommand
{
    public string CommandTypeName => CommandTypes.SalesInvoiceLineReturn;
    public string AggregateTypeName => "Invoice";
    public string? AggregateIdOrNull => InvoiceId;
}

public sealed class ReturnInvoiceLineCommandValidator : IValidator<ReturnInvoiceLineCommand>
{
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(ReturnInvoiceLineCommand c, CancellationToken ct) =>
        SalesDraftRules.Check(Rules.Required(c.InvoiceId, nameof(c.InvoiceId)),
            Rules.Required(c.LineId, nameof(c.LineId)), Rules.Required(c.Reason, nameof(c.Reason)));
}

public sealed class ReturnInvoiceLineCommandHandler(IInvoiceRepository invoices,
    IStockMovementRepository movements, ISalesPostingData posting, IClock clock)
    : ICommandHandler<ReturnInvoiceLineCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(ReturnInvoiceLineCommand c, CommandContext context, CancellationToken ct)
    {
        var found = await SalesDraftRules.Find(invoices, c.InvoiceId, ct);
        if (found.IsFailure) return Result<Unit>.Fail(found.Error);
        var invoice = found.Value!;
        var line = invoice.Lines.FirstOrDefault(l => l.LineId == c.LineId);
        if (line is null) return Result<Unit>.Fail(ErrorCodes.NotFound);
        var marker = $"Return line {line.LineId}: ";
        if (invoice.Adjustments.Any(a => a.Kind == AdjustmentKind.LineReturn && a.Reason.StartsWith(marker, StringComparison.Ordinal)))
            return Result<Unit>.Fail(ErrorCodes.InvoiceLineIndexStale);
        // A whole-line return credits its documented line net. A post-time invoice-level discount
        // remains on the original immutable document; the compensating amount is explicit.
        var now = clock.UtcNow;
        var adjustment = new InvoiceAdjustment(IdGenerator.NewId(now), AdjustmentKind.LineReturn,
            line.LineNet, marker + c.Reason.Trim(), now, context.UserId, context.OperationId, null);
        var credited = invoice.AddCreditNote(adjustment);
        if (credited.IsFailure) return Result<Unit>.Fail(credited.Error, credited.Detail);
        if (line.ProductId is not null && line.VariantId is not null)
        {
            var cost = await posting.GetLatestReceiptCostAsync(line.ProductId, line.VariantId, ct);
            var movement = StockMovement.Create(context.TenantId, line.ProductId, line.VariantId,
                MovementKind.ReturnIn, line.Quantity, cost, now, context.UserId,
                sourceEntityType: "Invoice", sourceEntityId: invoice.Id,
                operationId: context.OperationId, reason: c.Reason);
            if (movement.IsFailure) return Result<Unit>.Fail(movement.Error, movement.Detail);
            movements.Add(movement.Value!);
        }
        return Result<Unit>.Ok(Unit.Value);
    }
}
