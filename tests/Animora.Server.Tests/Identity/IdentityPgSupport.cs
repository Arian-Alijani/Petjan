using Microsoft.AspNetCore.DataProtection;
using Animora.Persistence.PostgreSql.Context;
using Animora.Persistence.PostgreSql.DependencyInjection;
using Animora.Server.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using System.Collections.Concurrent;

namespace Animora.Server.Tests.Identity;

/// <summary>A DI container over a real, freshly migrated PostgreSQL database (the real S04 composition).</summary>
internal sealed class IdentityPgHost : IAsyncDisposable
{
    private readonly ServiceProvider _root;

    private IdentityPgHost(ServiceProvider root, string connectionString)
    {
        _root = root;
        ConnectionString = connectionString;
    }

    // One host per test database: every host builds its own pooled NpgsqlDataSources, and EF keeps one internal
    // service provider per distinct data source (it throws after 20). Hosts live until the test process exits;
    // the fixture drops the database with FORCE.
    private static readonly ConcurrentDictionary<string, Lazy<Task<IdentityPgHost>>> Hosts = new(StringComparer.Ordinal);

    public string ConnectionString { get; }

    /// <summary>The shared host of a test database (migrated once).</summary>
    public static Task<IdentityPgHost> GetAsync(string connectionString) =>
        Hosts.GetOrAdd(connectionString, key => new Lazy<Task<IdentityPgHost>>(() => CreateAsync(key))).Value;

    private static async Task<IdentityPgHost> CreateAsync(string connectionString)
    {
        // The test fixture owns DDL (the running server role never migrates).
        var options = new DbContextOptionsBuilder<AnimoraServerDbContext>().UseNpgsql(connectionString).Options;
        await using (var db = new AnimoraServerDbContext(options, new Persistence.FixedTenantContext(null)))
        {
            await db.Database.MigrateAsync();
        }

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Database:ConnectionString"] = connectionString })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection().SetApplicationName("Animora.Server.Tests");
        services.AddAnimoraPostgreSql(configuration);
        services.AddAnimoraIdentity(configuration);
        return new IdentityPgHost(services.BuildServiceProvider(validateScopes: true), connectionString);
    }

    /// <summary>A new DI scope = one HTTP request worth of services.</summary>
    public AsyncServiceScope Scope() => _root.CreateAsyncScope();

    public async ValueTask DisposeAsync()
    {
        await _root.DisposeAsync();
        NpgsqlConnection.ClearAllPools();
    }
}
