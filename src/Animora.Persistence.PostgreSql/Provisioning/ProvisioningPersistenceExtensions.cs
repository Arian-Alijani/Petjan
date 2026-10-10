using Animora.Persistence.PostgreSql.DependencyInjection;
using Animora.Persistence.PostgreSql.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
namespace Animora.Persistence.PostgreSql.Provisioning;

public static class ProvisioningPersistenceExtensions
{
    public static IServiceCollection AddAnimoraProvisioningPersistence(this IServiceCollection services)
    {
        services.AddDbContext<ProvisioningDbContext>((sp, o) => o.UseNpgsql(sp.GetRequiredKeyedService<NpgsqlDataSource>(
            sp.GetRequiredService<WorkerExecutionContext>().IsWorker ? PersistenceServiceCollectionExtensions.WorkerKey : PersistenceServiceCollectionExtensions.OltpWriteKey)));
        services.AddScoped<IProvisioningIntentStore, ProvisioningIntentStore>(); services.AddScoped<WebsiteClientStore>(); return services;
    }
}
