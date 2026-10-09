using Animora.Persistence.PostgreSql.Identity;
using Animora.Persistence.PostgreSql.Tenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace Animora.Persistence.PostgreSql.DependencyInjection;

/// <summary>DI wiring of the S04 identity persistence (stores, tenant directory, token service, audit).</summary>
public static class IdentityPersistenceServiceCollectionExtensions
{
    /// <summary>
    /// Registers the identity persistence services. Must run <b>after</b>
    /// <see cref="PersistenceServiceCollectionExtensions.AddAnimoraPostgreSql"/>: it replaces the default
    /// no-tenant <see cref="ITenantContext"/> with the scoped, explicitly-bound <see cref="AuthTenantContext"/>
    /// (S05 replaces it again with the request-bound implementation).
    /// </summary>
    public static IServiceCollection AddAnimoraIdentityPersistence(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<AuthTenantContext>();
        services.Replace(ServiceDescriptor.Scoped<ITenantContext>(sp => sp.GetRequiredService<AuthTenantContext>()));

        // The only unfiltered tenant read: own keyless context on the read pool.
        services.AddDbContext<TenantDirectoryContext>((sp, options) => options
            .UseNpgsql(sp.GetRequiredKeyedService<NpgsqlDataSource>(PersistenceServiceCollectionExtensions.OltpReadKey))
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));
        services.AddScoped<ITenantDirectory, TenantDirectory>();

        services.AddScoped<IIdentityRepository, EfIdentityRepository>();
        services.AddScoped<IRefreshTokenStore, EfRefreshTokenStore>();
        services.AddScoped<RefreshTokenService>();
        services.AddScoped<ISecurityAuditWriter, SecurityAuditWriter>();
        services.AddScoped<ServerUserStore>();
        services.AddScoped<TenantProvisioner>();
        services.TryAddSingleton<IPasswordHasher<ServerIdentityUser>, PasswordHasher<ServerIdentityUser>>();

        return services;
    }
}
