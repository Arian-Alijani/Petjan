using Animora.Persistence.PostgreSql.Tenancy;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Animora.Persistence.PostgreSql.Context;

/// <summary>
/// Builds no-tracking read contexts on the <c>oltp-read</c> pool, bound to the current request's tenant.
/// Registered <b>scoped</b> so it captures the request-scoped <see cref="ITenantContext"/> and the two
/// tenancy interceptors; the keyed read data source it reads from is a process-wide singleton. Each
/// <see cref="Create"/> returns a fresh context the caller disposes.
/// </summary>
public sealed class ServerReadContextFactory(
    NpgsqlDataSource readDataSource,
    ITenantContext tenantContext,
    TenantStampingInterceptor stampingInterceptor,
    NoUnnamedIgnoreFiltersInterceptor ignoreFiltersInterceptor) : IServerReadContextFactory
{
    /// <inheritdoc />
    public AnimoraServerDbContext Create()
    {
        DbContextOptions<AnimoraServerDbContext> options =
            new DbContextOptionsBuilder<AnimoraServerDbContext>()
                .UseNpgsql(readDataSource)
                .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
                .AddInterceptors(stampingInterceptor, ignoreFiltersInterceptor)
                .Options;

        return new AnimoraServerDbContext(options, tenantContext);
    }
}
