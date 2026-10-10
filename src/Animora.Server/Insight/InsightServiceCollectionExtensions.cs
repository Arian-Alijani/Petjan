using Animora.Persistence.PostgreSql.Insight;
using Animora.Persistence.PostgreSql.Options;
using Animora.Server.Admin;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
namespace Animora.Server.Insight;
internal static class InsightServiceCollectionExtensions
{
    public static IServiceCollection AddAnimoraInsight(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<InsightOptions>().Bind(configuration.GetSection(InsightOptions.SectionName)).ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<InsightOptions>, InsightOptionsValidator>());
        if (!configuration.GetValue<bool>("Insight:Enabled"))
        {
            services.AddScoped<ITenantInsightQuery, DisabledTenantInsightQuery>();
            return services;
        }
        services.TryAddSingleton<IReportingQuerySessionFactory, ReportingQuerySessionFactory>();
        services.AddSingleton<IInsightReportingBudget, InsightReportingBudget>();
        services.AddSingleton<InsightCache>();
        services.AddInsightReadPersistence();
        services.AddScoped<IInsightOperatorContext, InsightOperatorContext>();
        services.AddScoped<ITenantInsightQuery, ReportingInsightQuery>();
        return services;
    }
    // Future features must also explicitly extend the approved source/metric catalogue. None at launch.
    public static IServiceCollection AddInsightMetric<T>(this IServiceCollection services) where T : class, IInsightMetricProvider
    {
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IInsightMetricProvider, T>());
        return services;
    }
}
