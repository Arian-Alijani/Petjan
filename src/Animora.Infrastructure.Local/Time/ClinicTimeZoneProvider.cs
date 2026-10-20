using Animora.Application.Ports;
using Animora.Domain.Identity;
using Animora.Persistence.Sqlite.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Animora.Infrastructure.Local.Time;

/// <summary>
/// Resolves the persisted clinic timezone; never captures a scoped database context. The resolved zone is cached for
/// the installation (one tenant per local store) and re-resolved after <see cref="Invalidate"/>, so repeated handler /
/// count lookups cost nothing and the UI path hydrates it asynchronously instead of querying on the caller's thread
/// (A23). The synchronous <see cref="GetClinicTimeZone"/> NEVER runs a blocking EF query: on an un-hydrated provider it
/// returns the default zone (Asia/Tehran — the product's market and <see cref="Tenant.DefaultTimezone"/>) without caching
/// it and kicks off one background hydrate, so the very next read returns the stored tenant zone (INF-05).
/// </summary>
public sealed class ClinicTimeZoneProvider(IServiceScopeFactory scopes) : IClinicTimeZoneProvider
{
    private volatile TimeZoneInfo? _cached;

    private int _hydrating;
    private TimeZoneInfo? _default;

    public TimeZoneInfo GetClinicTimeZone()
    {
        if (_cached is { } zone) return zone;

        // Cache miss on a possibly-UI caller: never open a scope / run SQLite here. Serve the default zone (uncached, so the
        // stored value still wins once hydrated) and hydrate off-thread exactly once at a time.
        if (Interlocked.CompareExchange(ref _hydrating, 1, 0) == 0)
        {
            _ = Task.Run(async () =>
            {
                try { await GetClinicTimeZoneAsync().ConfigureAwait(false); }
                catch (Exception ex) when (ex is not OutOfMemoryException) { /* store not ready: next read retries */ }
                finally { Volatile.Write(ref _hydrating, 0); }
            });
        }

        return _cached ?? (_default ??= Resolve(null));
    }

    public async ValueTask<TimeZoneInfo> GetClinicTimeZoneAsync(CancellationToken ct = default)
    {
        if (_cached is { } zone) return zone;
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        var id = await db.Set<Tenant>().AsNoTracking().Select(t => t.Timezone).FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        return _cached = Resolve(id);
    }

    public void Invalidate() => _cached = null;

    private static TimeZoneInfo Resolve(string? id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id ?? Tenant.DefaultTimezone); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.CreateCustomTimeZone("Asia/Tehran fallback", TimeSpan.FromMinutes(210),
                "Tehran", "Tehran");
        }
    }
}
