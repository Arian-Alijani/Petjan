using Animora.Persistence.PostgreSql.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Server.Hosting;

/// <summary>
/// Readiness probe: the OLTP database answers <c>SELECT 1</c> within 2 seconds and the
/// <c>animora_set_version()</c> trigger function exists (migration 101 applied). Any failure,
/// including "no connection string configured", degrades to Unhealthy — never an exception —
/// so the host still boots without PostgreSQL. Reports a flag only, never error text.
/// </summary>
internal sealed class DatabaseProbe(IServiceScopeFactory scopeFactory) : IReadinessProbe
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    public string Name => "database";

    public async Task<ReadinessResult> CheckAsync(CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AnimoraServerDbContext>();

            var reachable = await db.Database
                .SqlQueryRaw<int>("SELECT 1 AS \"Value\"")
                .ToListAsync(timeout.Token)
                .ConfigureAwait(false);
            if (reachable.Count != 1)
            {
                return ReadinessResult.Unhealthy();
            }

            var functions = await db.Database
                .SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM pg_proc WHERE proname = 'animora_set_version'")
                .ToListAsync(timeout.Token)
                .ConfigureAwait(false);

            return functions is [> 0] ? ReadinessResult.Healthy() : ReadinessResult.Unhealthy();
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            // Expected: unreachable DB, timeout, bad/missing connection string, missing permissions.
            return ReadinessResult.Unhealthy();
        }
    }
}
