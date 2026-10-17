using System.Globalization;
using Animora.Persistence.PostgreSql.DependencyInjection;
using Animora.Persistence.PostgreSql.Operator;
using Animora.Persistence.PostgreSql.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace Animora.Persistence.PostgreSql.Subscriptions;

public static class SubscriptionServiceCollectionExtensions
{
    public static IServiceCollection AddAnimoraSubscriptions(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<SubscriptionOptions>().Configure(o =>
        {
            var s = configuration.GetSection(SubscriptionOptions.SectionName);
            if (int.TryParse(s[nameof(o.GraceDays)], NumberStyles.Integer, CultureInfo.InvariantCulture, out var grace)) o.GraceDays = grace;
            if (long.TryParse(s[nameof(o.StandardMonthlyAmountMinor)], NumberStyles.Integer, CultureInfo.InvariantCulture, out var amount))
                o.StandardMonthlyAmountMinor = amount;
        }).Validate(o => o.GraceDays is >= 0 and <= 365 && o.StandardMonthlyAmountMinor >= 0).ValidateOnStart();
        services.AddDbContext<AnimoraOperatorDbContext>((sp, o) => o.UseNpgsql(sp.GetRequiredKeyedService<NpgsqlDataSource>(
            sp.GetRequiredService<WorkerExecutionContext>().IsWorker ? PersistenceServiceCollectionExtensions.WorkerKey
                : PersistenceServiceCollectionExtensions.OltpWriteKey)));
        services.TryAddScoped<ISubscriptionService, SubscriptionService>();
        services.TryAddScoped<ISeatUsageReader, SeatUsageReader>();
        services.TryAddScoped<ITenantAdministration, TenantAdministration>();
        services.TryAddScoped<ProvisioningEngine>();
        services.TryAddScoped<OwnerCredentialAdministration>();
        services.TryAddScoped<ClinicStaffAdministration>();
        services.TryAddScoped<ProvisioningReplayCredentials>();
        services.TryAddScoped<SubscriptionSweep>();
        services.TryAddScoped<SubscriptionExpiryProcessor>();
        services.TryAddScoped<ILicenseIssuanceScheduler, NoopLicenseIssuanceScheduler>();
        return services;
    }
}
