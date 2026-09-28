using Animora.Application.Ports;
using Animora.Domain.Identity;
using Animora.Persistence.Sqlite.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Animora.Infrastructure.Local.Time;

/// <summary>Resolves the persisted clinic timezone; never captures a scoped database context.</summary>
public sealed class ClinicTimeZoneProvider(IServiceScopeFactory scopes) : IClinicTimeZoneProvider
{
    public TimeZoneInfo GetClinicTimeZone()
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        var id = db.Set<Tenant>().AsNoTracking().Select(t => t.Timezone).FirstOrDefault()
            ?? Tenant.DefaultTimezone;
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.CreateCustomTimeZone("Asia/Tehran fallback", TimeSpan.FromMinutes(210),
                "Tehran", "Tehran");
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.CreateCustomTimeZone("Asia/Tehran fallback", TimeSpan.FromMinutes(210),
                "Tehran", "Tehran");
        }
    }
}
