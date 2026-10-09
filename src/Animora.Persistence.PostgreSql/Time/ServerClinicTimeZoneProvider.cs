using Animora.Application.Ports;
using Animora.Domain.Identity;
using Animora.Persistence.PostgreSql.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.PostgreSql.Time;

/// <summary>Request-scoped tenant timezone; never caches one clinic's zone across tenants.</summary>
public sealed class ServerClinicTimeZoneProvider(AnimoraServerDbContext db) : IClinicTimeZoneProvider
{
    private TimeZoneInfo? _cached;
    public TimeZoneInfo GetClinicTimeZone() => _cached ??= Resolve(
        db.Tenants.AsNoTracking().Select(t => t.Timezone).FirstOrDefault());

    public async ValueTask<TimeZoneInfo> GetClinicTimeZoneAsync(CancellationToken ct = default)
    {
        if (_cached is not null) return _cached;
        var id = await db.Tenants.AsNoTracking().Select(t => t.Timezone).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return _cached = Resolve(id);
    }
    public void Invalidate() => _cached = null;
    private static TimeZoneInfo Resolve(string? id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id ?? Tenant.DefaultTimezone); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(Tenant.DefaultTimezone); }
            catch (Exception fallback) when (fallback is TimeZoneNotFoundException or InvalidTimeZoneException)
            { return TimeZoneInfo.CreateCustomTimeZone("Asia/Tehran", TimeSpan.FromMinutes(210), "Tehran", "Tehran"); }
        }
    }
}
