using Animora.Application.Abstractions;
using Animora.Persistence.PostgreSql.Context;
using Animora.Persistence.PostgreSql.KeyRing;
using Animora.Persistence.PostgreSql.Options;
using Animora.Persistence.PostgreSql.Tenancy;
using Animora.Persistence.PostgreSql.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Animora.Persistence.PostgreSql.DependencyInjection;

/// <summary>DI entry point for the PostgreSQL persistence layer (the only place Npgsql is wired).</summary>
public static class PersistenceServiceCollectionExtensions
{
    /// <summary>Keyed <see cref="NpgsqlDataSource"/> key of the read/write OLTP pool.</summary>
    public const string OltpWriteKey = "oltp-write";

    /// <summary>Keyed <see cref="NpgsqlDataSource"/> key of the read-only OLTP pool (consumed from S03).</summary>
    public const string OltpReadKey = "oltp-read";

    /// <summary>Keyed <see cref="NpgsqlDataSource"/> key of the DataProtection key-ring pool.</summary>
    public const string KeyRingKey = "keyring";

    private const string ApplicationName = "Animora.Server";
    private const int CommandTimeoutSeconds = 30;

    /// <summary>
    /// Registers options (validated on start), the tenant context default, the three keyed data
    /// sources and both DbContexts. No data source connects until first use, so the host boots
    /// without PostgreSQL (readiness then reports not-ready). Transaction/retry policy lives in
    /// <c>ServerUnitOfWork</c> (S03), not in the Npgsql provider options.
    /// </summary>
    public static IServiceCollection AddAnimoraPostgreSql(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services
            .AddOptions<DatabaseOptions>()
            .Configure(o => BindOptions(configuration.GetSection(DatabaseOptions.SectionName), o))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<DatabaseOptions>, DatabaseOptionsValidator>());

        // Default: no tenant. S05 replaces this with the request-bound implementation.
        services.TryAddSingleton<ITenantContext, NullTenantContext>();
        services.TryAddScoped<TenantStampingInterceptor>();
        services.TryAddScoped<NoUnnamedIgnoreFiltersInterceptor>();

        services.AddKeyedSingleton<NpgsqlDataSource>(OltpWriteKey, (sp, _) => BuildDataSource(sp, o => o.OltpWriteMax));
        services.AddKeyedSingleton<NpgsqlDataSource>(OltpReadKey, (sp, _) => BuildDataSource(sp, o => o.OltpReadMax));
        services.AddKeyedSingleton<NpgsqlDataSource>(KeyRingKey, (sp, _) => BuildDataSource(sp, o => o.KeyRingMax));

        services.AddDbContext<AnimoraServerDbContext>((sp, options) => options
            .UseNpgsql(sp.GetRequiredKeyedService<NpgsqlDataSource>(OltpWriteKey))
            .AddInterceptors(
                sp.GetRequiredService<TenantStampingInterceptor>(),
                sp.GetRequiredService<NoUnnamedIgnoreFiltersInterceptor>()));

        services.AddDbContext<AnimoraKeyRingDbContext>((sp, options) => options
            .UseNpgsql(
                sp.GetRequiredKeyedService<NpgsqlDataSource>(KeyRingKey),
                npgsql => npgsql.MigrationsHistoryTable(AnimoraKeyRingDbContext.MigrationsHistoryTable)));

        // S03: transaction policy, RLS-ready session hook (off) and the no-tracking read factory.
        services.TryAddSingleton<ITenantSessionInitializer, NoopTenantSessionInitializer>();
        services.TryAddScoped<ITransactionRunner, ServerTransactionRunner>();
        services.TryAddScoped<IUnitOfWork, ServerUnitOfWork>();
        services.TryAddScoped<IServerReadContextFactory>(sp => new ServerReadContextFactory(
            sp.GetRequiredKeyedService<NpgsqlDataSource>(OltpReadKey),
            sp.GetRequiredService<ITenantContext>(),
            sp.GetRequiredService<TenantStampingInterceptor>(),
            sp.GetRequiredService<NoUnnamedIgnoreFiltersInterceptor>()));

        return services;
    }

    // Explicit binding keeps this project free of the reflection-based Configuration.Binder package.
    private static void BindOptions(IConfigurationSection section, DatabaseOptions options)
    {
        if (section["ConnectionString"] is { Length: > 0 } connectionString)
        {
            options.ConnectionString = connectionString;
        }

        options.OltpWriteMax = ReadInt(section, nameof(DatabaseOptions.OltpWriteMax), options.OltpWriteMax);
        options.OltpReadMax = ReadInt(section, nameof(DatabaseOptions.OltpReadMax), options.OltpReadMax);
        options.KeyRingMax = ReadInt(section, nameof(DatabaseOptions.KeyRingMax), options.KeyRingMax);
        options.ProfileGiB = ReadInt(section, nameof(DatabaseOptions.ProfileGiB), options.ProfileGiB);
    }

    private static int ReadInt(IConfigurationSection section, string key, int fallback) =>
        int.TryParse(section[key], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int value)
            ? value
            : fallback;

    private static NpgsqlDataSource BuildDataSource(IServiceProvider sp, Func<DatabaseOptions, int> maxPoolSize)
    {
        DatabaseOptions options = sp.GetRequiredService<IOptions<DatabaseOptions>>().Value;

        var builder = new NpgsqlConnectionStringBuilder(options.ConnectionString)
        {
            ApplicationName = ApplicationName,
            MaxPoolSize = maxPoolSize(options),
            MinPoolSize = 0,
            CommandTimeout = CommandTimeoutSeconds,
        };

        return new NpgsqlDataSourceBuilder(builder.ConnectionString).Build();
    }
}
