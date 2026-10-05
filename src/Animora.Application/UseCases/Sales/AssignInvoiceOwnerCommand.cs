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
[Auditable(AuditCategory.Financial, CommandTypes.SalesInvoiceOwnerSet)]
/// <summary>
/// Re-assigns who an open invoice is billed to: an owner record (<paramref name="OwnerId"/>) or, when it is
/// null, a walk-in named <paramref name="GuestName"/>.
/// </summary>
public sealed record AssignInvoiceOwnerCommand(string InvoiceId, string? OwnerId, string? PetId,
    string? GuestName = null)
    : ICommand<Unit>, IJournaledCommand
{
    public string CommandTypeName => CommandTypes.SalesInvoiceOwnerSet;
    public string AggregateTypeName => "Invoice";
    public string? AggregateIdOrNull => InvoiceId;
}
public sealed class AssignInvoiceOwnerCommandValidator : IValidator<AssignInvoiceOwnerCommand>
{
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(AssignInvoiceOwnerCommand c, CancellationToken ct) =>
        SalesDraftRules.Check(Rules.Required(c.InvoiceId, nameof(c.InvoiceId)),
            string.IsNullOrWhiteSpace(c.OwnerId) ? Rules.Required(c.GuestName, nameof(c.GuestName)) : null);
}
public sealed class AssignInvoiceOwnerCommandHandler(IInvoiceRepository invoices)
    : ICommandHandler<AssignInvoiceOwnerCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(AssignInvoiceOwnerCommand c, CommandContext context, CancellationToken ct)
    {
        var found = await SalesDraftRules.Find(invoices, c.InvoiceId, ct);
        if (found.IsFailure) return Result<Unit>.Fail(found.Error);
        return SalesDraftRules.Outcome(string.IsNullOrWhiteSpace(c.OwnerId)
            ? found.Value!.AssignGuest(c.GuestName!)
            : found.Value!.AssignOwner(c.OwnerId, c.PetId));
    }
}

