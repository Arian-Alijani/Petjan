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
/// (A23).
/// </summary>
public sealed class ClinicTimeZoneProvider(IServiceScopeFactory scopes) : IClinicTimeZoneProvider
{
    private volatile TimeZoneInfo? _cached;

    public TimeZoneInfo GetClinicTimeZone()
    {
        if (_cached is { } zone) return zone;
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        var id = db.Set<Tenant>().AsNoTracking().Select(t => t.Timezone).FirstOrDefault();
        return _cached = Resolve(id);
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
