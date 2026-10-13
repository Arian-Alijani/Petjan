using Animora.Application.Abstractions;
using Animora.Application.Auditing;
using Animora.Application.Commands;
using Animora.Application.Ports;
using Animora.Application.Security;
using Animora.Application.Validation;
using Animora.Contracts.Wire.Commands;
using Animora.Domain.Identity;
using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Domain.Sales;
using Animora.Domain.Time;

namespace Animora.Application.UseCases.Sales;

[RequiresPermission(PermissionCodes.SalesVoid)]
[Auditable(AuditCategory.Financial, CommandTypes.SalesInvoiceVoid)]
public sealed record VoidInvoiceCommand(string InvoiceId, string Reason) : ICommand<Unit>, IJournaledCommand
{
    public string CommandTypeName => CommandTypes.SalesInvoiceVoid;
    public string AggregateTypeName => "Invoice";
    public string? AggregateIdOrNull => InvoiceId;
}

public sealed class VoidInvoiceCommandValidator : IValidator<VoidInvoiceCommand>
{
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(VoidInvoiceCommand c, CancellationToken ct) =>
        SalesDraftRules.Check(Rules.Required(c.InvoiceId, nameof(c.InvoiceId)), Rules.Required(c.Reason, nameof(c.Reason)));
}

public sealed class VoidInvoiceCommandHandler(IInvoiceRepository invoices, IClock clock)
    : ICommandHandler<VoidInvoiceCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(VoidInvoiceCommand c, CommandContext context, CancellationToken ct)
    {
        var found = await SalesDraftRules.Find(invoices, c.InvoiceId, ct);
        if (found.IsFailure) return Result<Unit>.Fail(found.Error);
        var invoice = found.Value!;
        if (invoice.State == InvoiceState.Posted && invoice.DueTotal.Minor > 0)
        {
            var now = clock.UtcNow;
            var adjustment = new InvoiceAdjustment(IdGenerator.ForOperation(context.OperationId, 0, now), AdjustmentKind.CreditNote,
                invoice.DueTotal, c.Reason.Trim(), now, context.UserId, context.OperationId, null);
            var credited = invoice.AddCreditNote(adjustment);
            if (credited.IsFailure) return Result<Unit>.Fail(credited.Error, credited.Detail);
        }
        return SalesDraftRules.Outcome(invoice.Void(c.Reason, clock));
    }
}
