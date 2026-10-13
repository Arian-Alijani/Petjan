using System.Diagnostics.Metrics;
using Animora.Persistence.PostgreSql.Options;
using Animora.Persistence.PostgreSql.Replication;
using Animora.Server.Hosting;
using Animora.Server.Workers;
using Microsoft.Extensions.Options;
namespace Animora.Server.Observability;

internal sealed class SnapshotAgeMonitor : ResilientWorker,IReadinessProbe
{
    private readonly ServerDatabaseStatisticsReader _reader; private readonly ServerMetrics _metrics;
    private readonly IOptions<FeedStorageOptions> _options; private readonly IBackgroundAdmission _admission;
    private IReadOnlyDictionary<string,int> _pools=new Dictionary<string,int>();
    private readonly DatabasePoolUsage? _poolUsage;
    private bool _observed;
    public string Name=>"snapshot-age";
    public bool IsRequired=>false;
    public SnapshotAgeMonitor(ServerDatabaseStatisticsReader reader,ServerMetrics metrics,IOptions<FeedStorageOptions> options,
        IBackgroundAdmission admission,TimeProvider clock,WorkerHeartbeat heartbeat,ILogger<SnapshotAgeMonitor> logger,DatabasePoolUsage? poolUsage=null,
        IOptions<DatabaseOptions>? database=null)
        :base("snapshot-age",TimeSpan.FromSeconds(15),clock,heartbeat,logger)
    {
        _reader=reader; _metrics=metrics; _options=options; _admission=admission; _poolUsage=poolUsage;
        if(database is not null)
        {
            var d=database.Value;
            metrics.ConfigurePoolLimits(new Dictionary<string,int>
            {
                ["Animora.Server/oltp-write"]=d.OltpWriteMax,["Animora.Server/oltp-read"]=d.OltpReadMax,
                ["Animora.Server/keyring"]=d.KeyRingMax,["Animora.Server/worker"]=d.WorkerMax,
            },()=>Volatile.Read(ref _pools));
        }
        metrics.ObservePools(()=>Volatile.Read(ref _pools).Select(p=>new Measurement<int>(p.Value,new KeyValuePair<string,object?>("source",p.Key))));
        metrics.ObserveWorkers(()=>heartbeat.Ages().Select(p=>new Measurement<double>(p.Value,new KeyValuePair<string,object?>("worker",p.Key))));
    }
    internal Task RunOnceAsync(CancellationToken ct=default)=>TickAsync(ct);
    protected override async Task TickAsync(CancellationToken ct)
    {
        await using var lease=await _admission.EnterAsync(ct).ConfigureAwait(false);
        var stats=await _reader.ReadAsync(ct).ConfigureAwait(false);
        _metrics.SetSnapshotAge(stats.SnapshotAge); _metrics.SetQueues(stats.Pending,stats.Dead,stats.Intake);
        _metrics.SetBacklogs(stats.OutboxLagSeconds,stats.ExpiredLeases);
        var pools=_poolUsage?.Snapshot()??stats.ActiveBySource;
        _metrics.SetPoolInUse(pools.Values.Sum());
        Volatile.Write(ref _pools,pools); _observed=true;
    }
    public Task<ReadinessResult> CheckAsync(CancellationToken ct)=>Task.FromResult(new ReadinessResult(_observed&&_metrics.SnapshotAge<=_options.Value.MaxSnapshotLifetime.TotalSeconds*3));
}
internal sealed class DatabaseCapacityReadinessProbe(IDatabaseCapabilityProbe probe,IOptions<DatabaseOptions> options) : IReadinessProbe
{
    public string Name=>"database-capacity";
    public bool IsRequired=>false;
    public async Task<ReadinessResult> CheckAsync(CancellationToken ct)
    {
        try { return new(await probe.MaxConnectionsAsync(ct).ConfigureAwait(false)>=options.Value.PoolSum+8); }
        catch(Exception ex) when(ex is not OperationCanceledException) { return ReadinessResult.Unhealthy(); }
    }
}
