using Animora.Persistence.PostgreSql.Outbox;
using Animora.Persistence.PostgreSql.Replication;
using Animora.Application.Abstractions;
using Animora.Persistence.PostgreSql.Commands;
using Animora.Persistence.PostgreSql.Context;
using Animora.Persistence.PostgreSql.KeyRing;
using Animora.Persistence.PostgreSql.Options;
using Animora.Persistence.PostgreSql.Tenancy;
using Animora.Persistence.PostgreSql.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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

    public const string WorkerKey = "worker";

    private const string ApplicationName = "Animora.Server";
    private const int CommandTimeoutSeconds = 30;

    // RFC 2606 reserves ".invalid": DNS never resolves it, so an unconfigured data source cannot connect anywhere.
    private const string UnconfiguredConnectionString = "Host=database-not-configured.invalid;Timeout=2";

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
        // Interceptors are scoped (they capture the request's ITenantContext) and are registered as
        // IInterceptor so EF resolves them from the application service provider. Attaching per-scope
        // instances with AddInterceptors makes EF build a new internal service provider for every request
        // (an unbounded cache that throws ManyServiceProvidersCreatedWarning after twenty).
        services.TryAddScoped<TenantStampingInterceptor>();
        services.TryAddScoped<NoUnnamedIgnoreFiltersInterceptor>();
        services.AddScoped<IInterceptor>(sp => sp.GetRequiredService<TenantStampingInterceptor>());
        services.AddScoped<IInterceptor>(sp => sp.GetRequiredService<NoUnnamedIgnoreFiltersInterceptor>());

        services.AddKeyedSingleton<NpgsqlDataSource>(OltpWriteKey, (sp, _) => BuildDataSource(sp, o => o.OltpWriteMax, "oltp-write"));
        services.AddKeyedSingleton<NpgsqlDataSource>(OltpReadKey, (sp, _) => BuildDataSource(sp, o => o.OltpReadMax, "oltp-read"));
        services.AddKeyedSingleton<NpgsqlDataSource>(KeyRingKey, (sp, _) => BuildDataSource(sp, o => o.KeyRingMax, "keyring"));

        services.AddKeyedSingleton<NpgsqlDataSource>(WorkerKey, (sp, _) => BuildDataSource(sp, o => o.WorkerMax, "worker"));
        services.TryAddScoped<WorkerExecutionContext>();
        services.TryAddScoped<ServerOutbox>();
        services.TryAddScoped<OutboxClaimStore>();
        services.TryAddScoped<OutboxConsumerRunner>();
        services.TryAddSingleton<WorkerTenantDirectory>(sp => new(sp.GetRequiredKeyedService<NpgsqlDataSource>(WorkerKey)));
        services.AddDbContext<OutboxDbContext>((sp, o) => o.UseNpgsql(sp.GetRequiredKeyedService<NpgsqlDataSource>(WorkerKey)));

        services.AddDbContext<AnimoraServerDbContext>((sp, options) => options
            .UseNpgsql(sp.GetRequiredKeyedService<NpgsqlDataSource>(
                sp.GetRequiredService<WorkerExecutionContext>().IsWorker ? WorkerKey : OltpWriteKey)));

        services.AddDbContext<AnimoraKeyRingDbContext>((sp, options) => options
            .UseNpgsql(
                sp.GetRequiredKeyedService<NpgsqlDataSource>(KeyRingKey),
                npgsql => npgsql.MigrationsHistoryTable(AnimoraKeyRingDbContext.MigrationsHistoryTable)));

        // S03: transaction policy, RLS-ready session hook (off) and the no-tracking read factory.
        services.TryAddSingleton<ITenantSessionInitializer, NoopTenantSessionInitializer>();
        services.TryAddScoped<ITransactionRunner, ServerTransactionRunner>();
        services.TryAddScoped<IUnitOfWork, ServerUnitOfWork>();

        // S06: tenant-scoped idempotency store + retention compactor.
        services.TryAddScoped<ICommandIntakeStore, CommandIntakeStore>();
        services.TryAddScoped<CommandIntakeCompactor>();
        services.TryAddScoped<IServerReadContextFactory>(sp => new ServerReadContextFactory(
            sp.GetRequiredKeyedService<NpgsqlDataSource>(OltpReadKey),
            sp.GetRequiredService<ITenantContext>(),
            sp));

        services.AddOptions<FeedStorageOptions>();
        services.TryAddSingleton<IBackgroundAdmission>(_ => new SemaphoreBackgroundAdmission(1));
        services.TryAddSingleton<FeedPageWriter>();
        services.TryAddScoped<RecordProjector>();
        services.TryAddScoped<BatchFreezer>();
        services.TryAddScoped<FeedPageReader>();
        services.TryAddScoped<FeedRetentionService>();
        services.TryAddScoped<IFeedContextFactory>(sp => new FeedContextFactory(
            sp.GetRequiredKeyedService<NpgsqlDataSource>(WorkerKey), sp.GetRequiredService<ITenantContext>(), sp));
        services.TryAddScoped<IFeedPosition, FeedPositionReader>();
        services.TryAddScoped<IFeedEpoch, FeedEpochService>();

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
        options.WorkerMax = ReadInt(section, nameof(DatabaseOptions.WorkerMax), options.WorkerMax);
        options.ProfileGiB = ReadInt(section, nameof(DatabaseOptions.ProfileGiB), options.ProfileGiB);
    }

    private static int ReadInt(IConfigurationSection section, string key, int fallback) =>
        int.TryParse(section[key], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int value)
            ? value
            : fallback;

    private static NpgsqlDataSource BuildDataSource(IServiceProvider sp, Func<DatabaseOptions, int> maxPoolSize, string poolName)
    {
        DatabaseOptions options = sp.GetRequiredService<IOptions<DatabaseOptions>>().Value;

        // An unconfigured database must not crash host startup (singletons such as the snapshot-age
        // monitor resolve their data source eagerly). Npgsql rejects a host-less connection string at
        // Build(), so substitute a reserved, never-resolvable host: every use fails fast and the
        // readiness probes report the database as not-ready, exactly as for an unreachable server.
        string connectionString = string.IsNullOrWhiteSpace(options.ConnectionString)
            ? UnconfiguredConnectionString
            : options.ConnectionString;

        var builder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            ApplicationName = ApplicationName + "/" + poolName,
            MaxPoolSize = maxPoolSize(options),
            MinPoolSize = 0,
            CommandTimeout = CommandTimeoutSeconds,
        };

        return new NpgsqlDataSourceBuilder(builder.ConnectionString) { Name=ApplicationName+"/"+poolName }.Build();
    }
}
