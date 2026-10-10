namespace Animora.Server.Workers;

internal sealed class WorkerFailureLogGate(TimeProvider clock)
{
    private readonly Dictionary<string, DateTimeOffset> _last = new(StringComparer.Ordinal);
    public bool ShouldLog(string failureClass)
    {
        lock (_last)
        {
            var now = clock.GetUtcNow();
            if (_last.TryGetValue(failureClass, out var at) && now - at < TimeSpan.FromMinutes(1)) return false;
            if (_last.Count >= 64 && !_last.ContainsKey(failureClass)) _last.Remove(_last.OrderBy(x => x.Value).First().Key);
            _last[failureClass] = now; return true;
        }
    }
}
internal abstract class ResilientWorker : BackgroundService
{
    private readonly string _name;
    protected readonly TimeProvider Clock;
    protected readonly WorkerHeartbeat Heartbeat;
    protected readonly ILogger Logger;
    private readonly WorkerFailureLogGate _logs;
    protected TimeSpan Period;
    protected ResilientWorker(string name, TimeSpan period, TimeProvider clock, WorkerHeartbeat heartbeat, ILogger logger)
    {
        _name = name; Period = period; Clock = clock; Heartbeat = heartbeat; Logger = logger;
        _logs = new(clock); heartbeat.Register(name, period);
    }
    protected abstract Task TickAsync(CancellationToken ct);
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Period, Clock);
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await TickAsync(stoppingToken).ConfigureAwait(false); Heartbeat.Success(_name); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                var name = ex.GetType().Name;
                if (_logs.ShouldLog(name)) Logger.LogWarning("Worker {Worker} failure class {FailureClass}", _name, name);
            }
            timer.Period = Period; Heartbeat.Period(_name, Period);
            try { if (!await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false)) break; }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
