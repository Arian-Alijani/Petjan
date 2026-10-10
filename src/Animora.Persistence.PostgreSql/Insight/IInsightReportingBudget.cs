using Animora.Persistence.PostgreSql.Options;
namespace Animora.Persistence.PostgreSql.Insight;
public interface IInsightReportingBudget
{
    ValueTask<ReportingQuerySession> AcquireAsync(CancellationToken ct = default);
}
/// <summary>One reporting session at a time; its disposal releases the process-wide permit.</summary>
public sealed class InsightReportingBudget(IReportingQuerySessionFactory factory) : IInsightReportingBudget, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    public async ValueTask<ReportingQuerySession> AcquireAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var session = await factory.OpenAsync(ct).ConfigureAwait(false);
            session.ReleaseOnDispose(() => _gate.Release());
            return session;
        }
        catch { _gate.Release(); throw; }
    }
    public void Dispose() => _gate.Dispose();
}
