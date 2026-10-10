using Animora.Persistence.PostgreSql.Licensing;
using Animora.Persistence.PostgreSql.Outbox;
using Animora.Persistence.PostgreSql.Subscriptions;
using Animora.Server.Authorization;
using Animora.Server.Licensing.Issuance;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace Animora.Server.Licensing;

internal static class LicensingServiceCollectionExtensions
{
    public static IServiceCollection AddAnimoraLicensing(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<LicensingOptions>().Bind(configuration.GetSection(LicensingOptions.SectionName))
            .Validate(o => o.IsValid()).ValidateOnStart();
        services.AddAnimoraLicensingPersistence(); services.AddHttpContextAccessor();
        services.AddScoped<LicenseKeyManager>();
        services.AddScoped<ILicenseSignerProvider>(sp => sp.GetRequiredService<LicenseKeyManager>());
        services.Replace(ServiceDescriptor.Scoped<IServerEntitlementGate, ServerEntitlementGate>());
        services.Replace(ServiceDescriptor.Scoped<ILicenseIssuanceScheduler, OutboxLicenseIssuanceScheduler>());
        services.AddScoped<IOutboxConsumer, LicenseIssueConsumer>(); services.AddScoped<IOutboxConsumer, LicenseRecheckConsumer>();
        services.AddHostedService<LicenseKeyBootstrapper>();
        return services;
    }
}
