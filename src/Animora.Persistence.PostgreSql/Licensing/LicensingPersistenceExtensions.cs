using Animora.Persistence.PostgreSql.DependencyInjection;
using Animora.Persistence.PostgreSql.Subscriptions;
using Animora.Persistence.PostgreSql.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
namespace Animora.Persistence.PostgreSql.Licensing;

public static class LicensingPersistenceExtensions
{
    public static IServiceCollection AddAnimoraLicensingPersistence(this IServiceCollection services)
    {
        services.AddDbContext<LicenseStore>((sp, o) => o.UseNpgsql(sp.GetRequiredKeyedService<NpgsqlDataSource>(
            sp.GetRequiredService<WorkerExecutionContext>().IsWorker ? PersistenceServiceCollectionExtensions.WorkerKey : PersistenceServiceCollectionExtensions.OltpWriteKey)));
        services.AddDbContext<AnimoraEntitlementReadContext>((sp, o) => o.UseNpgsql(
            sp.GetRequiredKeyedService<NpgsqlDataSource>(PersistenceServiceCollectionExtensions.OltpReadKey)));
        services.AddScoped<LicenseAdministration>(); services.AddScoped<LicenseIssuer>(); services.AddScoped<LicenseGrantProjector>();
        services.AddScoped<SubscriptionOutbox>();
        return services;
    }
}
