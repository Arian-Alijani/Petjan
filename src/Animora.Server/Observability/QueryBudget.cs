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
internal sealed class QueryBudget(IReportingQuerySessionFactory sessions) : IQueryBudget,IDisposable
{
    private readonly SemaphoreSlim _reporting=new(1,1);
    public async ValueTask<QueryLease> Acquire(QueryClass queryClass,CancellationToken ct=default)
    {
        if(queryClass==QueryClass.Oltp) return new(null,null);
        await _reporting.WaitAsync(ct).ConfigureAwait(false);
        try { return new(await sessions.OpenAsync(ct).ConfigureAwait(false),()=>_reporting.Release()); }
        catch { _reporting.Release(); throw; }
    }
    public void Dispose()=>_reporting.Dispose();
}
