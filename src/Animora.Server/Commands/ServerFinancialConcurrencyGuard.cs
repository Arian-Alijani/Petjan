using Animora.Application.Commands;
using Animora.Application.UseCases.Cashier;
using Animora.Application.UseCases.Sales;
using Animora.Domain.Results;
using Animora.Persistence.PostgreSql.Commands;
using Animora.Persistence.PostgreSql.Repositories;

namespace Animora.Server.Commands;

internal sealed class ServerFinancialConcurrencyGuard(ServerSchedulingConcurrencyGuard scheduling,
    MoneyResourceLock locks, ServerCommandExecutionScope execution) : IServerCommandConcurrencyGuard
{
    public async Task<Result> PrepareAsync(object command, CancellationToken ct = default)
    {
        var resources = new List<string>();
        if (command is IJournaledCommand { AggregateTypeName: "Invoice", AggregateIdOrNull: { } invoiceId })
            resources.Add("invoice:" + invoiceId);
        if (command is OpenCashSessionCommand or CloseCashSessionCommand or RecordCashMovementCommand
            or RegisterPaymentCommand or RefundInvoiceCommand or ReturnInvoiceLineCommand)
            resources.Add("cashier:" + execution.UserId);
        await locks.AcquireAsync(resources, ct);
        return await scheduling.PrepareAsync(command, ct);
    }
}
