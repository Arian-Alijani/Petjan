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
using Animora.Domain.ValueObjects;

namespace Animora.Application.UseCases.Sales;

[RequiresPermission(PermissionCodes.SalesDraft)]
[Auditable(AuditCategory.Financial, CommandTypes.SalesInvoiceLineAdd)]
public sealed record AddInvoiceLineCommand(string InvoiceId, string CatalogItemId, decimal Quantity)
    : ICommand<Unit>, IJournaledCommand
{
    public string CommandTypeName => CommandTypes.SalesInvoiceLineAdd;
    public string AggregateTypeName => "Invoice";
    public string? AggregateIdOrNull => InvoiceId;
}
public sealed class AddInvoiceLineCommandValidator : IValidator<AddInvoiceLineCommand>
{
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(AddInvoiceLineCommand c, CancellationToken ct) =>
        SalesDraftRules.Check(Rules.Required(c.InvoiceId, nameof(c.InvoiceId)),
            Rules.Required(c.CatalogItemId, nameof(c.CatalogItemId)),
            Rules.Range(c.Quantity, 0.001m, 1000000m, nameof(c.Quantity)));
}
public sealed class AddInvoiceLineCommandHandler(IInvoiceRepository invoices, ISalesDraftData data)
    : ICommandHandler<AddInvoiceLineCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(AddInvoiceLineCommand c, CommandContext context, CancellationToken ct)
    {
        var found = await SalesDraftRules.Find(invoices, c.InvoiceId, ct);
        if (found.IsFailure) return Result<Unit>.Fail(found.Error);
        var item = await data.FindCatalogEntryAsync(c.CatalogItemId, ct);
        if (item is null) return Result<Unit>.Fail(ErrorCodes.NotFound);
        // Line id = OperationId: Edit/Remove commands address a line by id (ExpectedLineId); it must match on the server.
        var line = new InvoiceLine(context.OperationId, item.Kind, item.Id,
            item.ProductId, item.VariantId, item.Title, new Quantity(c.Quantity, item.UnitCode),
            Money.Irt(item.PriceMinor), 0, 0, InvoiceLineSource.Catalog, item.Id);
        return SalesDraftRules.Outcome(found.Value!.AddLine(line));
    }
}

