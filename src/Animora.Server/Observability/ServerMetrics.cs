using System.Diagnostics.Metrics;
namespace Animora.Server.Observability;

/// <summary>One low-cardinality catalogue: tenant/user/device identifiers are never metric labels.</summary>
internal sealed class ServerMetrics : IDisposable
{
    public const string MeterName = "Animora.Server";
    public Meter Meter { get; } = new(MeterName);
    public Counter<long> HttpRequests { get; }
    public Histogram<double> HttpDuration { get; }
    public Counter<long> CommandsSettled { get; }
    public Counter<long> RateLimitRejected { get; }
    public Counter<long> SyncBatches { get; }
    public Histogram<double> SyncDuration { get; }
    public Counter<long> SyncBytes { get; }
    public DateTimeOffset StartedUtc { get; }=DateTimeOffset.UtcNow;
    private int _poolInUse;
    public int PoolInUse=>Volatile.Read(ref _poolInUse);
    public void SetPoolInUse(int count)=>Volatile.Write(ref _poolInUse,count);
    private long _commands, _outboxPending, _deadLetters, _intake;
    private double _snapshotAge;
    private readonly Dictionary<string,Counter<long>> _reconciliation;
    public long CommandsInProgress => Interlocked.Read(ref _commands);
    public long OutboxPending => Interlocked.Read(ref _outboxPending);
    public long OutboxDeadLettered => Interlocked.Read(ref _deadLetters);
    public long IntakeInProgress => Interlocked.Read(ref _intake);
    public double SnapshotAge => Volatile.Read(ref _snapshotAge);
    public ServerMetrics()
    {
        HttpRequests=Meter.CreateCounter<long>("animora.http.requests"); HttpDuration=Meter.CreateHistogram<double>("animora.http.duration","ms");
        CommandsSettled=Meter.CreateCounter<long>("animora.commands.settled"); RateLimitRejected=Meter.CreateCounter<long>("animora.ratelimit.rejected");
        Meter.CreateObservableGauge("animora.commands.in_progress",()=>CommandsInProgress);
        Meter.CreateObservableGauge("animora.outbox.pending",()=>OutboxPending);
        Meter.CreateObservableGauge("animora.outbox.dead_lettered",()=>OutboxDeadLettered);
        Meter.CreateObservableGauge("animora.pg.snapshot.age.seconds",()=>SnapshotAge);
        SyncBatches=Meter.CreateCounter<long>("animora.sync.batch.created"); SyncDuration=Meter.CreateHistogram<double>("animora.sync.batch.duration","ms");
        SyncBytes=Meter.CreateCounter<long>("animora.sync.batch.bytes");
        _reconciliation=new[]{"abandoned","blob_missing","zero_ref","orphan","bitrot"}.ToDictionary(x=>x,x=>Meter.CreateCounter<long>("animora.files.reconcile."+x));
    }
    public void Reconcile(string finding) => _reconciliation[finding].Add(1);
    public IDisposable CommandLease() { Interlocked.Increment(ref _commands); return new Lease(()=>Interlocked.Decrement(ref _commands)); }
    public void SetQueues(long pending,long dead,long intake) { Interlocked.Exchange(ref _outboxPending,pending); Interlocked.Exchange(ref _deadLetters,dead); Interlocked.Exchange(ref _intake,intake); }
    public void SetSnapshotAge(double value) => Volatile.Write(ref _snapshotAge,value);
    public void ObserveWorkers(Func<IEnumerable<Measurement<double>>> callback) => Meter.CreateObservableGauge("animora.worker.last_success_age.seconds",callback);
    public void ObservePools(Func<IEnumerable<Measurement<int>>> callback) => Meter.CreateObservableGauge("animora.pg.pool.in_use",callback);
    private sealed class Lease(Action release) : IDisposable { private Action? _release=release; public void Dispose()=>Interlocked.Exchange(ref _release,null)?.Invoke(); }
    public void Dispose()=>Meter.Dispose();
}
