using Animora.Persistence.PostgreSql.Tenancy;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Animora.Persistence.PostgreSql.Context;

/// <summary>
/// Builds no-tracking read contexts on the <c>oltp-read</c> pool, bound to the current request's tenant.
/// Registered <b>scoped</b> so it captures the request-scoped <see cref="ITenantContext"/> and the two
/// tenancy interceptors; the keyed read data source it reads from is a process-wide singleton. Each
/// <see cref="Create"/> returns a fresh context the caller disposes. The tenancy interceptors are resolved from
/// the scope's service provider (registered as <c>IInterceptor</c>), not attached as instances.
/// </summary>
public sealed class ServerReadContextFactory(
    NpgsqlDataSource readDataSource,
    ITenantContext tenantContext,
    IServiceProvider scopedServices) : IServerReadContextFactory
{
    /// <inheritdoc />
    public AnimoraServerDbContext Create()
    {
        DbContextOptions<AnimoraServerDbContext> options =
            new DbContextOptionsBuilder<AnimoraServerDbContext>()
                .UseApplicationServiceProvider(scopedServices)
                .UseNpgsql(readDataSource)
                .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
                .Options;

        return new AnimoraServerDbContext(options, tenantContext);
    }
}
