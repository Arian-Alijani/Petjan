using Animora.Domain.Results;
using Animora.Persistence.PostgreSql.Operator;
using Animora.Persistence.PostgreSql.Options;
using Animora.Server.Hosting;
using Animora.Server.Observability;
using Animora.Server.Workers;
using Animora.Web.Admin.Models;
using Animora.Web.Admin.Ports;
using Microsoft.Extensions.Options;
namespace Animora.Server.Admin;

internal sealed class AdminStatusReader(OperatorReadQueries queries,ServerMetrics metrics,ReadinessSnapshot readiness,
    IEnumerable<IReadinessProbe> probes,WorkerHeartbeat workers,IOptions<DatabaseOptions> database,AdminSession session) : IAdminStatusReader
{
    public async Task<Result<AdminStatus>> GetStatusAsync(CancellationToken ct=default)
    {
        _=session.OperatorId;
        var counts=await queries.DashboardAsync(ct).ConfigureAwait(false); // exactly one bounded query
        return Result<AdminStatus>.Ok(new(probes.Select(p=>new NamedStatus(p.Name,readiness.Get(p.Name))).ToArray(),
            workers.Ages().Select(p=>new WorkerStatus(p.Key,p.Value)).ToArray(),
            new(metrics.CommandsInProgress,metrics.OutboxPending,metrics.OutboxDeadLettered,metrics.IntakeInProgress),
            new(metrics.PoolInUse,database.Value.PoolSum),typeof(Program).Assembly.GetName().Version?.ToString()??"unknown",
            (DateTimeOffset.UtcNow-metrics.StartedUtc).TotalSeconds,counts.Expiring,counts.PastDue));
    }
}
