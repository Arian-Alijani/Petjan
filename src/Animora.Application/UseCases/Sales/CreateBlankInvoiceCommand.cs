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
public sealed record CreateBlankInvoiceCommand(string? OwnerId, string? GuestName, string? PetId = null)
    : ICommand<string>, IJournaledCommand
{
    public string CommandTypeName => CommandTypes.SalesInvoiceCreate;
    public string AggregateTypeName => "Invoice";
    public string? AggregateIdOrNull => null;
}
public sealed class CreateBlankInvoiceCommandValidator : IValidator<CreateBlankInvoiceCommand>
{
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(CreateBlankInvoiceCommand c, CancellationToken ct) =>
        SalesDraftRules.Check(string.IsNullOrWhiteSpace(c.OwnerId) && string.IsNullOrWhiteSpace(c.GuestName)
            ? Rules.Required(c.GuestName, nameof(c.GuestName)) : null);
}
public sealed class CreateBlankInvoiceCommandHandler(IInvoiceRepository invoices, IClock clock)
    : ICommandHandler<CreateBlankInvoiceCommand, string>
{
    public Task<Result<string>> HandleAsync(CreateBlankInvoiceCommand c, CommandContext context, CancellationToken ct)
    {
        // A unique provisional number keeps drafts distinct; posting replaces it with the sequence.
        var created = Invoice.Create(context.TenantId, "draft-" + context.OperationId,
            InvoiceKind.DirectSale, clock.UtcNow, context.UserId, c.OwnerId, c.GuestName, c.PetId,
            operationId: context.OperationId);
        if (created.IsFailure) return Task.FromResult(Result<string>.Fail(created.Error, created.Detail));
        invoices.Add(created.Value!);
        return Task.FromResult(Result<string>.Ok(created.Value!.Id));
    }
}

