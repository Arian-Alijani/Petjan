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
[Auditable(AuditCategory.Financial, CommandTypes.SalesInvoiceDiscountSet)]
public sealed record SetInvoiceDiscountCommand(string InvoiceId, DiscountKind Kind, decimal Value)
    : ICommand<Unit>, IJournaledCommand
{
    public string CommandTypeName => CommandTypes.SalesInvoiceDiscountSet;
    public string AggregateTypeName => "Invoice";
    public string? AggregateIdOrNull => InvoiceId;
}
public sealed class SetInvoiceDiscountCommandValidator : IValidator<SetInvoiceDiscountCommand>
{
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(SetInvoiceDiscountCommand c, CancellationToken ct) =>
        SalesDraftRules.Check(Rules.Required(c.InvoiceId, nameof(c.InvoiceId)),
            Rules.ValidEnum(c.Kind, nameof(c.Kind)), Rules.Range(c.Value, 0m, c.Kind == DiscountKind.Percent ? 100m : decimal.MaxValue, nameof(c.Value)));
}
public sealed class SetInvoiceDiscountCommandHandler(IInvoiceRepository invoices)
    : ICommandHandler<SetInvoiceDiscountCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(SetInvoiceDiscountCommand c, CommandContext context, CancellationToken ct)
    {
        var found = await SalesDraftRules.Find(invoices, c.InvoiceId, ct);
        return found.IsFailure ? Result<Unit>.Fail(found.Error)
            : SalesDraftRules.Outcome(found.Value!.SetDiscount(c.Kind, c.Value));
    }
}

