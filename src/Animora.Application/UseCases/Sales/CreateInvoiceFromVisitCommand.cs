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
[Auditable(AuditCategory.Financial, CommandTypes.SalesInvoiceCreate)]
public sealed record CreateInvoiceFromVisitCommand(string HandoffId) : ICommand<string>, IJournaledCommand
{
    public string CommandTypeName => CommandTypes.SalesInvoiceCreate;
    public string AggregateTypeName => "Invoice";
    public string? AggregateIdOrNull => null;
}
public sealed class CreateInvoiceFromVisitCommandValidator : IValidator<CreateInvoiceFromVisitCommand>
{
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(CreateInvoiceFromVisitCommand c, CancellationToken ct) =>
        SalesDraftRules.Check(Rules.Required(c.HandoffId, nameof(c.HandoffId)));
}
public sealed class CreateInvoiceFromVisitCommandHandler(ISalesDraftData data, IInvoiceRepository invoices, IClock clock)
    : ICommandHandler<CreateInvoiceFromVisitCommand, string>
{
    public Task<Result<string>> HandleAsync(CreateInvoiceFromVisitCommand c, CommandContext context, CancellationToken ct) =>
        SalesHandoffFactory.CreateAsync(data, invoices, clock, "Visit", c.HandoffId, context, ct);
}

