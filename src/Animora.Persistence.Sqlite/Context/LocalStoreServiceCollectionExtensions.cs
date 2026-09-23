using Animora.Domain.Time;
using Animora.Persistence.Sqlite.Options;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Animora.Persistence.Sqlite.Context;

public static class LocalStoreServiceCollectionExtensions
{
    public static IServiceCollection AddAnimoraLocalStore(this IServiceCollection services,
        Action<LocalStoreOptions>? configure = null)
    {
        services.AddOptions<LocalStoreOptions>();
        if (configure is not null) services.Configure(configure);
        services.AddScoped<ILocalTenantContext, SingleTenantContext>();
        services.AddSingleton<IStoreKeyProvider, NullStoreKeyProvider>();
        services.AddScoped<SqliteConnectionInterceptor>();
        services.AddDbContext<AnimoraLocalDbContext>((provider, builder) =>
        {
            var store = provider.GetRequiredService<IOptions<LocalStoreOptions>>().Value;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(store.DatabasePath))!);
            var connection = new SqliteConnectionStringBuilder { DataSource = store.DatabasePath }.ToString();
            builder.UseSqlite(connection)
                .AddInterceptors(provider.GetRequiredService<SqliteConnectionInterceptor>())
                .EnableSensitiveDataLogging(false)
                .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
        });
        return services;
    }
}
