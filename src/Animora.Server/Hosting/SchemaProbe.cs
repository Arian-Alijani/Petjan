using Animora.Persistence.PostgreSql;
using Animora.Persistence.PostgreSql.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Server.Hosting;

/// <summary>
/// Readiness probe: the applied migration head equals <see cref="ServerSchemaVersion.ExpectedHead"/>.
/// The runtime never calls <c>Migrate()</c> (its role has no DDL rights); this is the only
/// schema check. Any failure degrades to Unhealthy. Reports a flag only.
/// </summary>
internal sealed class SchemaProbe(IServiceScopeFactory scopeFactory) : IReadinessProbe
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    public string Name => "schema";

    public async Task<ReadinessResult> CheckAsync(CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AnimoraServerDbContext>();

            var applied = await db.Database.GetAppliedMigrationsAsync(timeout.Token).ConfigureAwait(false);
            var head = applied.LastOrDefault();

            return ServerSchemaVersion.IsExpectedHead(head) ? ReadinessResult.Healthy() : ReadinessResult.Unhealthy();
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return ReadinessResult.Unhealthy();
        }
    }
}
