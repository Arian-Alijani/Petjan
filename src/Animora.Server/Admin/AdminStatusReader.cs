using System.Security.Claims;
using Animora.Domain.Results;
using Animora.Persistence.PostgreSql.Operator;
using Animora.Persistence.PostgreSql.Options;
using Animora.Server.Hosting;
using Animora.Server.Observability;
using Animora.Server.Workers;
using Animora.Web.Admin.Models;
using Animora.Web.Admin.Ports;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Options;
namespace Animora.Server.Admin;
internal interface IAdminStatusSnapshotSource { Task<AdminStatus> ReadAsync(CancellationToken ct = default); }
internal sealed class AdminStatusSnapshotSource(OperatorReadQueries queries, ServerMetrics metrics, ReadinessSnapshot readiness,
    IEnumerable<IReadinessProbe> probes, WorkerHeartbeat workers, IOptions<DatabaseOptions> database, TimeProvider time) : IAdminStatusSnapshotSource
{
    public async Task<AdminStatus> ReadAsync(CancellationToken ct = default)
    {
        var counts = await queries.DashboardAsync(ct).ConfigureAwait(false);
        return new(probes.Select(p => new NamedStatus(p.Name, readiness.Get(p.Name))).ToArray(),
            workers.Ages().Select(p => new WorkerStatus(p.Key, p.Value)).ToArray(),
            new(metrics.CommandsInProgress, metrics.OutboxPending, metrics.OutboxDeadLettered, metrics.IntakeInProgress),
            new(metrics.PoolInUse, database.Value.PoolSum), typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown",
            (time.GetUtcNow() - metrics.StartedUtc).TotalSeconds, counts.Expiring, counts.PastDue);
    }
}
/// <summary>SSR and circuit readers consume one cached snapshot, never query persistence per panel/tick.</summary>
internal sealed class AdminStatusReader(AdminStatusCache cache,
    AuthenticationStateProvider authentication, TimeProvider time) : IAdminStatusReader
{
    public async Task<Result<AdminStatus>> GetStatusAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var principal = (await authentication.GetAuthenticationStateAsync().ConfigureAwait(false)).User;
        if (!principal.Identities.Any(i => i.IsAuthenticated && i.AuthenticationType == OperatorCookie.Scheme) ||
            !principal.IsInRole("operator") || string.IsNullOrWhiteSpace(principal.FindFirstValue("sub")))
            return Result<AdminStatus>.Fail(ErrorCodes.Forbidden);
        // Circuits outlive HttpContext. Bound their snapshot access to the same absolute cookie expiry.
        if (!long.TryParse(principal.FindFirstValue("op_exp"), out var expiry) ||
            time.GetUtcNow().ToUnixTimeSeconds() >= expiry) return Result<AdminStatus>.Fail(ErrorCodes.Forbidden);
        return cache.Read();
    }
}
