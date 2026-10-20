using Animora.Domain.Results;
using Animora.Persistence.PostgreSql.Options;
using Animora.Persistence.PostgreSql.Outbox;
using Animora.Persistence.PostgreSql.Replication;
using Animora.Server.Hosting;
using Animora.Server.Resilience;
using Microsoft.Extensions.Options;
namespace Animora.Server.Workers;

internal sealed class OutboxRelayWorker : ResilientWorker
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IBackgroundAdmission _admission;
    private readonly CircuitBreaker _breaker;
    private readonly OutboundTimeout _timeout;
    private readonly WorkerSettings _settings;
    // PERF-MC-01: the effective consumer count auto-scales with CPU cores when Server:Workers:OutboxConsumers
    // is left at 0 (auto), but is hard-clamped to the worker DB-connection budget (Database:WorkerMax) so the
    // fan-out never opens more scopes than the worker pool can serve.
    private readonly int _outboxConsumers;
    // BUG-OUTBOX-01: claim size and consumer count are options-driven (Server:Workers). The relay runs on the WORKER
    // admission gate only (never the request-path sync gate) and holds it just for the claim, not for delivery.
    public OutboxRelayWorker(IServiceScopeFactory scopes, IBackgroundAdmission admission, CircuitBreaker breaker,
        OutboundTimeout timeout, IOptions<ServerOptions> options, IOptions<DatabaseOptions> database,
        TimeProvider clock, WorkerHeartbeat heartbeat, ILogger<OutboxRelayWorker> logger)
        : base("outbox-relay", TimeSpan.FromSeconds(1), clock, heartbeat, logger)
    {
        _scopes = scopes; _admission = admission; _breaker = breaker; _timeout = timeout;
        _settings = options.Value.Workers;
        _outboxConsumers = _settings.EffectiveOutboxConsumers(database.Value.WorkerMax);
    }

    protected override async Task TickAsync(CancellationToken ct)
    {
        IReadOnlyList<OutboxMessage> messages;
        // The permit covers only the claim query; delivery runs after release so a slow consumer never
        // pins the worker gate (and never the sync gate, which is a distinct semaphore).
        await using (var admission = await _admission.EnterAsync(ct).ConfigureAwait(false))
        await using (var scope = _scopes.CreateAsyncScope())
            // The validator guarantees ceil(batch / consumers) * delivery timeout stays below the 60-second
            // claim lease, so claimed rows cannot expire before they are delivered.
            messages = await scope.ServiceProvider.GetRequiredService<OutboxClaimStore>()
                .ClaimAsync(Clock.GetUtcNow(), _settings.OutboxClaimBatch, ct).ConfigureAwait(false);
        // A full batch means a backlog: re-tick immediately-ish; an empty claim backs off exponentially to 15 s.
        Period = messages.Count == 0 ? TimeSpan.FromSeconds(Math.Min(15, Period.TotalSeconds * 2)) : TimeSpan.FromSeconds(1);
        if (messages.Count == 0) return;
        var queue = new BoundedWorkerQueue<OutboxMessage>();
        foreach (var message in messages) await queue.WriteAsync(message, ct).ConfigureAwait(false);
        queue.Complete();
        await Task.WhenAll(Enumerable.Range(0, _outboxConsumers)
            .Select(_ => Consume(queue, ct))).ConfigureAwait(false);
    }
    private async Task Consume(BoundedWorkerQueue<OutboxMessage> queue, CancellationToken ct)
    {
        await foreach (var m in queue.ReadAllAsync(ct).ConfigureAwait(false))
        {
            await using var scope = _scopes.CreateAsyncScope();
            var sp = scope.ServiceProvider; var store = sp.GetRequiredService<OutboxClaimStore>();
            var consumers = sp.GetServices<IOutboxConsumer>().Where(c => c.MessageType == m.MessageType).ToArray();
            if (consumers.Length != 1)
            {
                if (await store.FailAsync(m, Clock.GetUtcNow(), TimeSpan.Zero, true, "unknown_or_ambiguous_message_type", ct).ConfigureAwait(false) == 0)
                    Logger.LogWarning("{Code}", ErrorCodes.CommandClaimLost.Code);
                continue;
            }
            Result result;
            if (!_breaker.TryEnter(out var permit)) result = Result.Fail(ErrorCodes.DispatchFailed, "circuit_open");
            else using (permit)
            {
                try
                {
                    result = await _timeout.RunAsync(token => sp.GetRequiredService<OutboxConsumerRunner>()
                        .RunAsync(consumers[0], m, Clock.GetUtcNow(), token), ct).ConfigureAwait(false);
                    if (result.IsSuccess) permit!.Succeed(); else permit!.Fail();
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex) { permit!.Fail(); result = Result.Fail(ErrorCodes.DispatchFailed, ex.GetType().Name); }
            }
            if (result.IsFailure && result.Error == ErrorCodes.CommandClaimLost) { Logger.LogWarning("{Code}", result.Error.Code); continue; }
            int updated = result.IsSuccess ? await store.CompleteAsync(m, Clock.GetUtcNow(), ct).ConfigureAwait(false)
                : await store.FailAsync(m, Clock.GetUtcNow(), ServerRetryPolicy.NextDelay(m.Attempts, Random.Shared.NextDouble),
                    m.Attempts >= ServerRetryPolicy.MaxAttempts, result.Error.Code, ct).ConfigureAwait(false);
            if (updated == 0) Logger.LogWarning("{Code}", ErrorCodes.CommandClaimLost.Code);
        }
    }
}
