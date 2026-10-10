using Animora.Persistence.PostgreSql.Provisioning;
namespace Animora.Server.Provisioning;

internal static class ProvisioningServiceCollectionExtensions
{
    public static IServiceCollection AddAnimoraProvisioning(this IServiceCollection services)
    {
        services.AddAnimoraProvisioningPersistence(); services.AddSingleton<ReplayWindow>();
        services.AddScoped<RequestSignatureVerifier>(); services.AddScoped<WebsiteClientService>();
        services.AddHostedService<ProvisioningIntentWorker>(); services.AddHostedService<ProvisioningIntentRetentionWorker>(); return services;
    }
}
