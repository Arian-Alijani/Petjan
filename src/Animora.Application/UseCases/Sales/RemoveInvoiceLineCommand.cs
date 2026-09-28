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
[Auditable(AuditCategory.Financial, CommandTypes.SalesInvoiceLineRemove)]
public sealed record RemoveInvoiceLineCommand(string InvoiceId, int Index, string ExpectedLineId)
    : ICommand<Unit>, IJournaledCommand
{
    public string CommandTypeName => CommandTypes.SalesInvoiceLineRemove;
    public string AggregateTypeName => "Invoice";
    public string? AggregateIdOrNull => InvoiceId;
}
public sealed class RemoveInvoiceLineCommandValidator : IValidator<RemoveInvoiceLineCommand>
{
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(RemoveInvoiceLineCommand c, CancellationToken ct) =>
        SalesDraftRules.Check(Rules.Required(c.InvoiceId, nameof(c.InvoiceId)), Rules.Required(c.ExpectedLineId, nameof(c.ExpectedLineId)));
}
public sealed class RemoveInvoiceLineCommandHandler(IInvoiceRepository invoices)
    : ICommandHandler<RemoveInvoiceLineCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(RemoveInvoiceLineCommand c, CommandContext context, CancellationToken ct)
    {
        var found = await SalesDraftRules.Find(invoices, c.InvoiceId, ct);
        if (found.IsFailure) return Result<Unit>.Fail(found.Error);
        var line = SalesDraftRules.LineAt(found.Value!, c.Index, c.ExpectedLineId);
        return line.IsFailure ? Result<Unit>.Fail(line.Error)
            : SalesDraftRules.Outcome(found.Value!.RemoveLine(line.Value!.LineId));
    }
}

