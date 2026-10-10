using Animora.Application.Abstractions;
using Animora.Domain.Events;
using Animora.Domain.Results;
using Microsoft.Extensions.DependencyInjection;
namespace Animora.Persistence.PostgreSql.Repositories;

/// <summary>Explicit, in-transaction event dispatch; financial events fail closed when unbound.</summary>
public sealed class ServerDomainEventDispatcher(IServiceProvider services) : IDomainEventDispatcher
{
    public async Task<Result> DispatchAsync(DomainEvent domainEvent, CancellationToken ct = default)
    {
        var result = domainEvent switch
        {
            InvoicePostedEvent e => await DispatchAsync<InvoicePostedEvent>(e, ct),
            InvoiceVoidedEvent e => await DispatchAsync<InvoiceVoidedEvent>(e, ct),
            PaymentRegisteredEvent e => await DispatchAsync<PaymentRegisteredEvent>(e, ct),
            InvoiceRefundedEvent e => await DispatchAsync<InvoiceRefundedEvent>(e, ct),
            GoodsReceiptPostedEvent e => await DispatchAsync<GoodsReceiptPostedEvent>(e, ct),
            StockMovementRecordedEvent e => await DispatchAsync<StockMovementRecordedEvent>(e, ct),
            ChequeClearedEvent e => await DispatchAsync<ChequeClearedEvent>(e, ct),
            ChequeBouncedEvent e => await DispatchAsync<ChequeBouncedEvent>(e, ct),
            CashSessionClosedEvent e => await DispatchAsync<CashSessionClosedEvent>(e, ct),
            ExpensePostedEvent e => await DispatchAsync<ExpensePostedEvent>(e, ct),
            _ => Result.Ok()
        };
        if (result.IsFailure) return result;
        foreach (var handler in services.GetServices<IDomainEventHandler<DomainEvent>>())
        {
            result = await handler.HandleAsync(domainEvent, ct);
            if (result.IsFailure) return result;
        }
        return Result.Ok();
    }
    private async Task<Result> DispatchAsync<T>(T domainEvent, CancellationToken ct) where T : DomainEvent
    {
        var handlers = services.GetServices<IDomainEventHandler<T>>().ToArray();
        if (handlers.Length == 0) return Result.Fail(ErrorCodes.HandlerNotRegistered, typeof(T).Name);
        foreach (var handler in handlers)
        {
            var result = await handler.HandleAsync(domainEvent, ct);
            if (result.IsFailure) return result;
        }
        return Result.Ok();
    }
}
