using Animora.Application.Abstractions;
using Animora.Application.Auditing;
using Animora.Application.Commands;
using Animora.Application.Ports;
using Animora.Application.Security;
using Animora.Application.UseCases.Sales;
using Animora.Contracts.Wire.Commands;
using Animora.Domain.Identity;
using Animora.Domain.Results;

namespace Animora.Server.Handlers;

// Visit and stay commands have identical wire shape. Resolve the source from tenant-filtered data,
// never by changing the client contract or registering two ambiguous candidates.
[RequiresPermission(PermissionCodes.SalesDraft)]
[Auditable(AuditCategory.Financial, CommandTypes.SalesInvoiceCreate)]
internal sealed record CreateInvoiceFromHandoffCommand(string HandoffId) : ICommand<string>, IJournaledCommand
{
    public string CommandTypeName => CommandTypes.SalesInvoiceCreate;
    public string AggregateTypeName => "Invoice";
    public string? AggregateIdOrNull => null;
}

internal sealed class CreateInvoiceFromHandoffCommandHandler(ISalesDraftData data,
    CreateInvoiceFromVisitCommandHandler visits, CreateInvoiceFromStayCommandHandler stays)
    : ICommandHandler<CreateInvoiceFromHandoffCommand, string>
{
    public async Task<Result<string>> HandleAsync(CreateInvoiceFromHandoffCommand command, CommandContext context, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command.HandoffId)) return Result<string>.Fail(ErrorCodes.ValidationFailed);
        var visit = await data.FindHandoffAsync("Visit", command.HandoffId, ct);
        var stay = await data.FindHandoffAsync("Boarding", command.HandoffId, ct);
        if (visit is not null && stay is not null) return Result<string>.Fail(ErrorCodes.ValidationFailed, "Ambiguous handoff source.");
        if (visit is not null) return await visits.HandleAsync(new(command.HandoffId), context, ct);
        if (stay is not null) return await stays.HandleAsync(new(command.HandoffId), context, ct);
        return Result<string>.Fail(ErrorCodes.NotFound);
    }
}
