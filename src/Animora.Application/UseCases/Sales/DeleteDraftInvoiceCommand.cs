using System.Text.Json;
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
using Animora.Domain.ValueObjects;

namespace Animora.Application.UseCases.Sales;

[RequiresPermission(PermissionCodes.SalesDraft)]
[Auditable(AuditCategory.Financial, CommandTypes.SalesInvoiceDelete)]
public sealed record DeleteDraftInvoiceCommand(string InvoiceId) : ICommand<Unit>, IJournaledCommand
{
    public string CommandTypeName => CommandTypes.SalesInvoiceDelete;
    public string AggregateTypeName => "Invoice";
    public string? AggregateIdOrNull => InvoiceId;
}
public sealed class DeleteDraftInvoiceCommandValidator : IValidator<DeleteDraftInvoiceCommand>
{
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(DeleteDraftInvoiceCommand c, CancellationToken ct) =>
        SalesDraftRules.Check(Rules.Required(c.InvoiceId, nameof(c.InvoiceId)));
}
public sealed class DeleteDraftInvoiceCommandHandler(IInvoiceRepository invoices)
    : ICommandHandler<DeleteDraftInvoiceCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(DeleteDraftInvoiceCommand c, CommandContext context, CancellationToken ct)
    {
        var found = await SalesDraftRules.Find(invoices, c.InvoiceId, ct);
        if (found.IsFailure) return Result<Unit>.Fail(found.Error);
        if (found.Value!.State != InvoiceState.Draft) return Result<Unit>.Fail(ErrorCodes.InvoiceImmutable);
        found.Value.Deleted = true;
        return Result<Unit>.Ok(Unit.Value);
    }
}
