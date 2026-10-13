using Animora.Persistence.PostgreSql.Options;
using Animora.Persistence.PostgreSql.DependencyInjection;
namespace Animora.Server.Observability;

internal enum QueryClass { Oltp, Reporting }
internal interface IQueryBudget { ValueTask<QueryLease> Acquire(QueryClass queryClass,CancellationToken ct=default); }
internal sealed class QueryLease(ReportingQuerySession? reporting,Action? release) : IAsyncDisposable,IDisposable
{
    public ReportingQuerySession? Reporting { get; }=reporting;
    private int _disposed;
    public async ValueTask DisposeAsync()
    {
        if(Interlocked.Exchange(ref _disposed,1)!=0) return;
        try { if(Reporting is not null) await Reporting.DisposeAsync().ConfigureAwait(false); } finally { release?.Invoke(); }
    }
    public void Dispose()=>DisposeAsync().AsTask().GetAwaiter().GetResult();
}
/// <summary>The reporting budget stayed saturated for the whole bounded wait; callers shed with 503 + Retry-After.</summary>
internal sealed class QueryBudgetExceededException() : Exception("Reporting query budget saturated.");

/// <summary>
/// Reporting-query admission (PERF-QB-01). Reporting sessions run on the dedicated worker pool, so the
/// concurrency is sized from options (<c>Server:QueryBudget:ReportingMax</c>, clamped to that pool's size) instead of
/// a hardcoded 1 — several tenants' reports can run side by side. A waiter that cannot get a permit within
/// <c>WaitSeconds</c> fails with <see cref="QueryBudgetExceededException"/> (mapped to a typed 503) rather than queueing
/// indefinitely behind one slow report. OLTP never takes a permit. Per-process by design.
/// </summary>
internal sealed class QueryBudget : IQueryBudget,IDisposable
{
    private readonly IReportingQuerySessionFactory _sessions;
    private readonly SemaphoreSlim _reporting;
    private readonly TimeSpan _wait;
    private long _waitTicks, _timeouts, _waits;
    public QueryBudget(IReportingQuerySessionFactory sessions,int reportingMax=1,TimeSpan? wait=null)
    {
        ArgumentNullException.ThrowIfNull(sessions); ArgumentOutOfRangeException.ThrowIfLessThan(reportingMax,1);
        _sessions=sessions; ReportingMax=reportingMax; _reporting=new(reportingMax,reportingMax); _wait=wait??TimeSpan.FromSeconds(10);
    }
    public int ReportingMax { get; }
    /// <summary>Permits currently held (for the scaling-signal gauges).</summary>
    public int ReportingInUse=>ReportingMax-_reporting.CurrentCount;
    /// <summary>Total time spent waiting for a reporting permit, in milliseconds (IMP-04).</summary>
    public double WaitMilliseconds=>TimeSpan.FromTicks(Interlocked.Read(ref _waitTicks)).TotalMilliseconds;
    public long Waits=>Interlocked.Read(ref _waits);
    public long Timeouts=>Interlocked.Read(ref _timeouts);
    public async ValueTask<QueryLease> Acquire(QueryClass queryClass,CancellationToken ct=default)
    {
        if(queryClass==QueryClass.Oltp) return new(null,null);
        var started=System.Diagnostics.Stopwatch.GetTimestamp();
        bool acquired;
        try { acquired=await _reporting.WaitAsync(_wait,ct).ConfigureAwait(false); }
        finally { Interlocked.Add(ref _waitTicks,System.Diagnostics.Stopwatch.GetElapsedTime(started).Ticks); Interlocked.Increment(ref _waits); }
        if(!acquired) { Interlocked.Increment(ref _timeouts); throw new QueryBudgetExceededException(); }
        try { return new(await _sessions.OpenAsync(ct).ConfigureAwait(false),()=>_reporting.Release()); }
        catch { _reporting.Release(); throw; }
    }
    public void Dispose()=>_reporting.Dispose();
}
