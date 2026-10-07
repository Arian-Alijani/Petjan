using System.Text.Json;
using Animora.Application.Finance;
using Animora.Domain.Events;
using Animora.Domain.Results;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Entities;
using Animora.Persistence.Sqlite.Outbox;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Animora.Infrastructure.Local.Workers;

/// <summary>A single managed desktop worker. Each message's fact changes and outbox ack share one save.</summary>
public sealed class FinanceProjectionWorker(IServiceScopeFactory scopes, ILogger<FinanceProjectionWorker> logger)
    : IAsyncDisposable
{
    // Cached, shared across every deserialize on the hot projection path. Default options keep exact
    // round-trip parity with the outbox writer (OutboxPayload.Serialize) which also uses defaults.
    private static readonly JsonSerializerOptions SerializerOptions = new();

    private readonly CancellationTokenSource _stop = new();
    private Task? _loop;

    public void Start() => _loop ??= Task.Run(() => RunAsync(_stop.Token));

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                IReadOnlyList<OutboxMessage> batch;
                await using (var scope = scopes.CreateAsyncScope())
                {
                    var claimed = await scope.ServiceProvider.GetRequiredService<OutboxClaim>()
                        .ClaimAsync(100, ct: ct).ConfigureAwait(false);
                    batch = claimed.IsSuccess ? claimed.Value! : [];
                }
                foreach (var message in batch)
                {
                    ct.ThrowIfCancellationRequested();
                    await ProcessAsync(message, ct).ConfigureAwait(false);
                }
                if (batch.Count == 0)
                    await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "Finance projection worker failed; retrying after backoff");
                try { await Task.Delay(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false); }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            }
        }
    }

    private async Task ProcessAsync(OutboxMessage message, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var claim = provider.GetRequiredService<OutboxClaim>();
        try
        {
            var domainEvent = Deserialize(message);
            if (domainEvent is not null)
                await provider.GetRequiredService<FinanceProjectionHandler>()
                    .HandleAsync(domainEvent, ct).ConfigureAwait(false);
            var complete = await claim.CompleteAsync(message.Id, ct).ConfigureAwait(false);
            if (complete.IsFailure) throw new InvalidOperationException($"Outbox ack: {complete.Error.Code}");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "Finance projection failed for outbox message {MessageId}", message.Id);
            // Never persist half-applied facts when reporting an unsuccessful attempt.
            provider.GetRequiredService<AnimoraLocalDbContext>().ChangeTracker.Clear();
            var failed = await claim.FailAsync(message.Id, ErrorCodes.Unexpected,
                TimeSpan.FromSeconds(Math.Min(300, 5 * (message.Attempts + 1))), ct).ConfigureAwait(false);
            if (failed.IsFailure) logger.LogError("Could not retry outbox message {MessageId}: {Code}",
                message.Id, failed.Error.Code);
        }
    }

    private DomainEvent? Deserialize(OutboxMessage m) => m.MessageType switch
    {
        nameof(InvoicePostedEvent) => JsonSerializer.Deserialize<InvoicePostedEvent>(m.PayloadJson, SerializerOptions),
        nameof(InvoiceVoidedEvent) => JsonSerializer.Deserialize<InvoiceVoidedEvent>(m.PayloadJson, SerializerOptions),
        nameof(PaymentRegisteredEvent) => JsonSerializer.Deserialize<PaymentRegisteredEvent>(m.PayloadJson, SerializerOptions),
        nameof(InvoiceRefundedEvent) => JsonSerializer.Deserialize<InvoiceRefundedEvent>(m.PayloadJson, SerializerOptions),
        nameof(GoodsReceiptPostedEvent) => JsonSerializer.Deserialize<GoodsReceiptPostedEvent>(m.PayloadJson, SerializerOptions),
        nameof(StockMovementRecordedEvent) => JsonSerializer.Deserialize<StockMovementRecordedEvent>(m.PayloadJson, SerializerOptions),
        nameof(ChequeClearedEvent) => JsonSerializer.Deserialize<ChequeClearedEvent>(m.PayloadJson, SerializerOptions),
        nameof(ChequeBouncedEvent) => JsonSerializer.Deserialize<ChequeBouncedEvent>(m.PayloadJson, SerializerOptions),
        nameof(CashSessionClosedEvent) => JsonSerializer.Deserialize<CashSessionClosedEvent>(m.PayloadJson, SerializerOptions),
        nameof(ExpensePostedEvent) => JsonSerializer.Deserialize<ExpensePostedEvent>(m.PayloadJson, SerializerOptions),
        _ => LogAndDrop(m),
    };

    // An unregistered MessageType is still drained (the outbox has no other consumer), but never silently:
    // in a financial domain a lost event must leave a trace so a missing projection registration is visible.
    private DomainEvent? LogAndDrop(OutboxMessage m)
    {
        logger.LogWarning(
            "Outbox message {MessageId} has unregistered type {MessageType}; draining without projection",
            m.Id, m.MessageType);
        return null;
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        if (_loop is not null) await _loop.ConfigureAwait(false);
        _stop.Dispose();
    }
}
