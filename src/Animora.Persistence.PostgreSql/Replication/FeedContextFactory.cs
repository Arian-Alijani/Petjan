using Animora.Persistence.PostgreSql.Context;
using Animora.Persistence.PostgreSql.Tenancy;
using Microsoft.EntityFrameworkCore;
using Npgsql;
namespace Animora.Persistence.PostgreSql.Replication;

public interface IFeedContextFactory { AnimoraServerDbContext Create(); }
public sealed class FeedContextFactory(NpgsqlDataSource source, ITenantContext tenant, IServiceProvider services) : IFeedContextFactory
{
    public AnimoraServerDbContext Create() => new(new DbContextOptionsBuilder<AnimoraServerDbContext>()
        .UseApplicationServiceProvider(services).UseNpgsql(source).UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking).Options, tenant);
}
