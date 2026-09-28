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
[Auditable(AuditCategory.Financial, CommandTypes.SalesInvoiceLineAdd)]
public sealed record AddCustomInvoiceLineCommand(string InvoiceId, string Title, long UnitPriceMinor, decimal Quantity)
    : ICommand<Unit>, IJournaledCommand
{
    public string CommandTypeName => CommandTypes.SalesInvoiceLineAdd;
    public string AggregateTypeName => "Invoice";
    public string? AggregateIdOrNull => InvoiceId;
}
public sealed class AddCustomInvoiceLineCommandValidator : IValidator<AddCustomInvoiceLineCommand>
{
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(AddCustomInvoiceLineCommand c, CancellationToken ct) =>
        SalesDraftRules.Check(Rules.Required(c.InvoiceId, nameof(c.InvoiceId)),
            Rules.Required(c.Title, nameof(c.Title)), Rules.Range(c.UnitPriceMinor, 0L, long.MaxValue, nameof(c.UnitPriceMinor)),
            Rules.Range(c.Quantity, 0.001m, 1000000m, nameof(c.Quantity)));
}
public sealed class AddCustomInvoiceLineCommandHandler(IInvoiceRepository invoices, IClock clock)
    : ICommandHandler<AddCustomInvoiceLineCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(AddCustomInvoiceLineCommand c, CommandContext context, CancellationToken ct)
    {
        var found = await SalesDraftRules.Find(invoices, c.InvoiceId, ct);
        if (found.IsFailure) return Result<Unit>.Fail(found.Error);
        return SalesDraftRules.Outcome(found.Value!.AddLine(new InvoiceLine(IdGenerator.NewId(clock.UtcNow),
            InvoiceLineKind.Custom, null, null, null, c.Title.Trim(), new Quantity(c.Quantity, "PCS"),
            Money.Irt(c.UnitPriceMinor), 0, 0, InvoiceLineSource.Manual, null)));
    }
}

