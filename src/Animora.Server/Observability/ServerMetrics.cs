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
    private long _commands, _outboxPending, _deadLetters, _intake, _expiredLeases;
    private double _outboxLag;
    private Func<IEnumerable<Measurement<double>>>? _reportingBudget;
    private double _snapshotAge;
    private readonly Dictionary<string,Counter<long>> _reconciliation;
    public long CommandsInProgress => Interlocked.Read(ref _commands);
    public long OutboxPending => Interlocked.Read(ref _outboxPending);
    public long OutboxDeadLettered => Interlocked.Read(ref _deadLetters);
    public long IntakeInProgress => Interlocked.Read(ref _intake);
    public double SnapshotAge => Volatile.Read(ref _snapshotAge);
    /// <summary>Age in seconds of the oldest due outbox row (IMP-04 outbox lag).</summary>
    public double OutboxLagSeconds => Volatile.Read(ref _outboxLag);
    /// <summary>InProgress intake rows with an expired lease: the intake-recovery backlog (IMP-04).</summary>
    public long ExpiredIntakeLeases => Interlocked.Read(ref _expiredLeases);
    public ServerMetrics()
    {
        HttpRequests=Meter.CreateCounter<long>("animora.http.requests"); HttpDuration=Meter.CreateHistogram<double>("animora.http.duration","ms");
        CommandsSettled=Meter.CreateCounter<long>("animora.commands.settled"); RateLimitRejected=Meter.CreateCounter<long>("animora.ratelimit.rejected");
        Meter.CreateObservableGauge("animora.commands.in_progress",()=>CommandsInProgress);
        Meter.CreateObservableGauge("animora.outbox.pending",()=>OutboxPending);
        Meter.CreateObservableGauge("animora.outbox.dead_lettered",()=>OutboxDeadLettered);
        Meter.CreateObservableGauge("animora.pg.snapshot.age.seconds",()=>SnapshotAge);
        // IMP-04 scaling signals (all low-cardinality; no tenant/user labels).
        Meter.CreateObservableGauge("animora.outbox.lag.seconds",()=>OutboxLagSeconds);
        Meter.CreateObservableGauge("animora.intake.recovery.backlog",()=>ExpiredIntakeLeases);
        Meter.CreateObservableGauge("animora.pg.pool.saturation",()=>PoolSaturation());
        Meter.CreateObservableGauge("animora.reporting.budget",()=>_reportingBudget is null?Array.Empty<Measurement<double>>():_reportingBudget());
        Meter.CreateObservableCounter("animora.authz.queries",()=>Interlocked.Read(ref _authzQueries),"{query}");
        Meter.CreateObservableCounter("animora.authz.duration.seconds.total",()=>AuthzSeconds,"s");
        SyncBatches=Meter.CreateCounter<long>("animora.sync.batch.created"); SyncDuration=Meter.CreateHistogram<double>("animora.sync.batch.duration","ms");
        SyncBytes=Meter.CreateCounter<long>("animora.sync.batch.bytes");
        _reconciliation=new[]{"abandoned","blob_missing","zero_ref","orphan","bitrot"}.ToDictionary(x=>x,x=>Meter.CreateCounter<long>("animora.files.reconcile."+x));
    }
    public void Reconcile(string finding) => _reconciliation[finding].Add(1);
    public IDisposable CommandLease() { Interlocked.Increment(ref _commands); return new Lease(()=>Interlocked.Decrement(ref _commands)); }
    public void SetQueues(long pending,long dead,long intake) { Interlocked.Exchange(ref _outboxPending,pending); Interlocked.Exchange(ref _deadLetters,dead); Interlocked.Exchange(ref _intake,intake); }
    public void SetSnapshotAge(double value) => Volatile.Write(ref _snapshotAge,value);
    public void SetBacklogs(double outboxLagSeconds,long expiredLeases) { Volatile.Write(ref _outboxLag,Math.Max(0,outboxLagSeconds)); Interlocked.Exchange(ref _expiredLeases,Math.Max(0,expiredLeases)); }

    private IReadOnlyDictionary<string,int> _poolLimits=new Dictionary<string,int>();
    private Func<IReadOnlyDictionary<string,int>>? _poolUsed;
    /// <summary>Registers the pool capacity (per source) so <c>animora.pg.pool.saturation</c> = in-use / max can be reported.</summary>
    public void ConfigurePoolLimits(IReadOnlyDictionary<string,int> limits,Func<IReadOnlyDictionary<string,int>> used) { _poolLimits=limits; _poolUsed=used; }
    private IEnumerable<Measurement<double>> PoolSaturation()
    {
        var used=_poolUsed?.Invoke(); if(used is null) yield break;
        foreach(var (source,max) in _poolLimits)
            if(max>0) yield return new Measurement<double>(Math.Min(1.0,used.GetValueOrDefault(source)/(double)max),new KeyValuePair<string,object?>("source",source));
    }

    /// <summary>Exposes the reporting-budget wait/in-use/timeout counters (A5) as <c>animora.reporting.budget{stat=...}</c>.</summary>
    public void ObserveReportingBudget(QueryBudget budget) => _reportingBudget=()=>
    [
        new Measurement<double>(budget.ReportingInUse,new KeyValuePair<string,object?>("stat","in_use")),
        new Measurement<double>(budget.ReportingMax,new KeyValuePair<string,object?>("stat","max")),
        new Measurement<double>(budget.WaitMilliseconds/1000.0,new KeyValuePair<string,object?>("stat","wait_seconds_total")),
        new Measurement<double>(budget.Waits,new KeyValuePair<string,object?>("stat","waits_total")),
        new Measurement<double>(budget.Timeouts,new KeyValuePair<string,object?>("stat","timeouts_total")),
    ];

    private long _authzQueries; private long _authzTicks;
    public double AuthzSeconds=>TimeSpan.FromTicks(Interlocked.Read(ref _authzTicks)).TotalSeconds;
    /// <summary>Records one authorization-pipeline DB lookup (membership/entitlement) and its latency (IMP-04).</summary>
    public void RecordAuthzQuery(TimeSpan elapsed) { Interlocked.Increment(ref _authzQueries); Interlocked.Add(ref _authzTicks,elapsed.Ticks); }
    public long AuthzQueries=>Interlocked.Read(ref _authzQueries);
    public void ObserveWorkers(Func<IEnumerable<Measurement<double>>> callback) => Meter.CreateObservableGauge("animora.worker.last_success_age.seconds",callback);
    public void ObservePools(Func<IEnumerable<Measurement<int>>> callback) => Meter.CreateObservableGauge("animora.pg.pool.in_use",callback);
    private sealed class Lease(Action release) : IDisposable { private Action? _release=release; public void Dispose()=>Interlocked.Exchange(ref _release,null)?.Invoke(); }
    public void Dispose()=>Meter.Dispose();
}
