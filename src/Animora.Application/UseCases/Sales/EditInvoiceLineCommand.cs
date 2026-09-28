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
[Auditable(AuditCategory.Financial, CommandTypes.SalesInvoiceLineEdit)]
public sealed record EditInvoiceLineCommand(string InvoiceId, int Index, string ExpectedLineId,
    decimal Quantity, decimal DiscountPercent) : ICommand<Unit>, IJournaledCommand
{
    public string CommandTypeName => CommandTypes.SalesInvoiceLineEdit;
    public string AggregateTypeName => "Invoice";
    public string? AggregateIdOrNull => InvoiceId;
}
public sealed class EditInvoiceLineCommandValidator : IValidator<EditInvoiceLineCommand>
{
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(EditInvoiceLineCommand c, CancellationToken ct) =>
        SalesDraftRules.Check(Rules.Required(c.InvoiceId, nameof(c.InvoiceId)),
            Rules.Required(c.ExpectedLineId, nameof(c.ExpectedLineId)),
            Rules.Range(c.Quantity, 0.001m, 1000000m, nameof(c.Quantity)),
            Rules.Range(c.DiscountPercent, 0m, 100m, nameof(c.DiscountPercent)));
}
public sealed class EditInvoiceLineCommandHandler(IInvoiceRepository invoices)
    : ICommandHandler<EditInvoiceLineCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(EditInvoiceLineCommand c, CommandContext context, CancellationToken ct)
    {
        var found = await SalesDraftRules.Find(invoices, c.InvoiceId, ct);
        if (found.IsFailure) return Result<Unit>.Fail(found.Error);
        var line = SalesDraftRules.LineAt(found.Value!, c.Index, c.ExpectedLineId);
        if (line.IsFailure) return Result<Unit>.Fail(line.Error);
        return SalesDraftRules.Outcome(found.Value!.EditLine(line.Value! with
        {
            Quantity = line.Value.Quantity with { Value = c.Quantity }, DiscountPercent = c.DiscountPercent,
        }));
    }
}

