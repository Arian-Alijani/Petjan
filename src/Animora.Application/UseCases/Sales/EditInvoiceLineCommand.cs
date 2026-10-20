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
public sealed class EditInvoiceLineCommandHandler(IInvoiceRepository invoices, ISalesDraftData data)
    : ICommandHandler<EditInvoiceLineCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(EditInvoiceLineCommand c, CommandContext context, CancellationToken ct)
    {
        var found = await SalesDraftRules.Find(invoices, c.InvoiceId, ct);
        if (found.IsFailure) return Result<Unit>.Fail(found.Error);
        var line = SalesDraftRules.LineAt(found.Value!, c.Index, c.ExpectedLineId);
        if (line.IsFailure) return Result<Unit>.Fail(line.Error);

        // Server-side stock gate, symmetric with AddInvoiceLineCommand: raising the quantity of a stock-tracked
        // variant line can never take the invoice's total of that variant beyond the units on hand. The edited
        // line's own quantity is REPLACED by the new one (not added to it); the other lines of the variant stay.
        var variantId = line.Value!.VariantId;
        if (variantId is not null && c.Quantity > line.Value.Quantity.Value)
        {
            var item = await data.FindCatalogEntryAsync(line.Value.CatalogItemId ?? variantId, ct);
            if (item is { TrackStock: true })
            {
                const long stockScale = 1000L;
                var otherLines = found.Value!.Lines
                    .Where(l => l.VariantId == variantId && l.LineId != line.Value.LineId)
                    .Sum(l => l.Quantity.Value);
                var requestedScaled = (long)System.Math.Ceiling((otherLines + c.Quantity) * stockScale);
                if (requestedScaled > item.OnHandScaled)
                {
                    return Result<Unit>.Fail(ErrorCodes.InsufficientStock);
                }
            }
        }

        return SalesDraftRules.Outcome(found.Value!.EditLine(line.Value! with
        {
            Quantity = line.Value.Quantity with { Value = c.Quantity }, DiscountPercent = c.DiscountPercent,
        }));
    }
}

