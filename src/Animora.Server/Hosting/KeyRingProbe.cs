using Animora.Persistence.PostgreSql.KeyRing;
using Microsoft.EntityFrameworkCore;

namespace Animora.Server.Hosting;

/// <summary>
/// Readiness probe: the DataProtection key ring is readable. Zero keys is still healthy (the
/// first boot creates one); an unreachable ring is Unhealthy — there is no in-memory fallback
/// (A-RULE 027). Reports a flag only.
/// </summary>
internal sealed class KeyRingProbe(IServiceScopeFactory scopeFactory) : IReadinessProbe
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    public string Name => "keyring";

    public async Task<ReadinessResult> CheckAsync(CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AnimoraKeyRingDbContext>();

            _ = await db.DataProtectionKeys.AnyAsync(timeout.Token).ConfigureAwait(false);
            return ReadinessResult.Healthy();
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return ReadinessResult.Unhealthy();
        }
    }
}
