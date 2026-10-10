using Animora.Persistence.PostgreSql.Operator;
using Animora.Persistence.PostgreSql.Tenancy;
using Animora.Server.Authorization;
using Animora.Server.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Animora.Server.Operator;

internal static class OperatorServiceCollectionExtensions
{
    public static IServiceCollection AddAnimoraOperator(this IServiceCollection services)
    {
        services.AddAuthentication().AddBearerToken(OperatorAuth.Scheme, o => o.BearerTokenExpiration = OperatorAuth.Lifetime);
        services.AddHttpContextAccessor();
        services.AddScoped<OperatorTenantContext>();
        services.Replace(ServiceDescriptor.Scoped<ITenantContext>(sp =>
            (sp.GetRequiredService<IHttpContextAccessor>().HttpContext?.Request.Path.StartsWithSegments(ServerRoutes.Operator,
                StringComparison.OrdinalIgnoreCase) == true || sp.GetRequiredService<IHttpContextAccessor>().HttpContext?.Request.Path.StartsWithSegments(ServerRoutes.Admin,
                StringComparison.OrdinalIgnoreCase) == true) ? sp.GetRequiredService<OperatorTenantContext>()
                : sp.GetRequiredService<RequestTenantContext>()));
        services.AddSingleton<IPasswordHasher<OperatorAccount>, PasswordHasher<OperatorAccount>>();
        services.AddSingleton<OperatorDummyCredential>();
        services.AddScoped<OperatorAccountService>();
        services.AddHostedService<OperatorBootstrapper>();
        return services;
    }
}
