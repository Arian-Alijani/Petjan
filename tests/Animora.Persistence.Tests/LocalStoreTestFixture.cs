using System;
using System.Data.Common;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Animora.Contracts.Diagnostics;
using Animora.Domain.Time;
using Animora.Infrastructure.Local;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Options;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Animora.Persistence.Tests;

/// <summary>
/// A no-op <see cref="IUserNotificationPort"/> so the store's integrity/crash seams (B05a/B05c) can
/// resolve in tests that don't assert on notifications. Registered with <c>TryAdd</c> so a test can
/// still supply its own recording double via <c>configureServices</c>.
/// </summary>
public sealed class NoOpUserNotificationPort : IUserNotificationPort
{
    public void Notify(NotificationSeverity severity, string code, string? entityRef,
        NotificationAction? action)
    {
        // Intentionally empty: the default persistence fixture does not observe notifications.
    }
}

/// <summary>A deterministic <see cref="IClock"/> for the persistence tests (no wall-clock — CONVENTIONS §9).</summary>
public sealed class FakeClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset Now { get; set; } = now;

    public DateTimeOffset UtcNow => Now;
}

/// <summary>An <see cref="ILocalTenantContext"/> fixed to one tenant, so a test can act as any tenant.</summary>
public sealed class FixedTenantContext(string tenantId) : ILocalTenantContext
{
    public string TenantId { get; set; } = tenantId;
}

/// <summary>
/// The shared persistence fixture (A09's owed item): a temp directory, a unique <b>file</b> database
/// (never <c>:memory:</c> — CONVENTIONS §1), a real DI container wired by the local infrastructure
/// entry point, and a dispose that clears the SQLite pools and deletes the database and its
/// <c>-wal</c>/<c>-shm</c> sidecars.
/// </summary>
public sealed class LocalStoreTestFixture : IDisposable
{
    public const string DefaultTenant = "T-LOCAL-DEFAULT";

    private readonly Action<LocalStoreOptions>? _configureOptions;
    private readonly Action<IServiceCollection>? _configureServices;
    private readonly ServiceProvider _services;
    private bool _disposed;
    private readonly bool _followInstallation;

    public LocalStoreTestFixture(
        string tenantId = DefaultTenant,
        Action<LocalStoreOptions>? configureOptions = null,
        Action<IServiceCollection>? configureServices = null,
        DateTimeOffset? now = null, bool followInstallation = false)
    {
        TenantId = tenantId;
        _followInstallation = followInstallation;
        _configureOptions = configureOptions;
        _configureServices = configureServices;
        Directory = Path.Combine(Path.GetTempPath(), "animora-a12-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(Directory);
        DatabasePath = Path.Combine(Directory, "animora.db");
        Clock = new FakeClock(now ?? new DateTimeOffset(2026, 3, 1, 9, 0, 0, TimeSpan.Zero));
        _services = BuildProvider();
        Options = _services.GetRequiredService<IOptions<LocalStoreOptions>>().Value;
    }

    public string TenantId { get; }

    public string Directory { get; }

    public string DatabasePath { get; }

    public string BackupDirectory => Options.BackupDirectory;

    public FakeClock Clock { get; }

    public LocalStoreOptions Options { get; }

    public IServiceProvider Services => _services;

    /// <summary>Creates a DI scope; dispose it to release the scoped <see cref="AnimoraLocalDbContext"/>.</summary>
    public IServiceScope CreateScope() => _services.CreateScope();

    /// <summary>Builds another provider over the <b>same</b> database file (e.g. to prove persistence across restarts).</summary>
    public ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IClock>(Clock);
        _configureServices?.Invoke(services);
        // The store's integrity/crash seams (B05a/B05c) depend on IUserNotificationPort, which the
        // desktop host supplies. Provide a no-op default with TryAdd so tests that pass their own
        // recording double via configureServices keep priority.
        services.TryAddSingleton<IUserNotificationPort, NoOpUserNotificationPort>();
        services.AddAnimoraLocalInfrastructure(options =>
        {
            options.DatabasePath = DatabasePath;
            _configureOptions?.Invoke(options);
        });
        if (!_followInstallation) services.AddScoped<ILocalTenantContext>(_ => new FixedTenantContext(TenantId));
        return services.BuildServiceProvider();
    }

    /// <summary>Applies every migration to the (empty) file database.</summary>
    public async Task MigrateAsync(CancellationToken ct = default)
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
        await db.Database.MigrateAsync(ct);
    }

    /// <summary>Runs the real 8-step local store startup (open, assert native, quick_check, migrate, checkpoint, seed, diagnostics).</summary>
    public async Task<Animora.Persistence.Sqlite.Maintenance.LocalStoreDiagnostics> InitializeAsync(CancellationToken ct = default)
    {
        var result = await TryInitializeAsync(ct);
        Assert.True(result.IsSuccess, result.Detail);
        return result.Value!;
    }

    /// <summary>Runs the 8-step startup and returns its raw result — for the failure-path assertions.</summary>
    public async Task<Animora.Domain.Results.Result<Animora.Persistence.Sqlite.Maintenance.LocalStoreDiagnostics>>
        TryInitializeAsync(CancellationToken ct = default)
    {
        using var scope = CreateScope();
        var initializer = scope.ServiceProvider
            .GetRequiredService<Animora.Persistence.Sqlite.Maintenance.ILocalStoreInitializer>();
        return await initializer.InitializeAsync(ct);
    }

    /// <summary>Runs the startup through the concrete type so the internal step log can be read (A12).</summary>
    public async Task<(Animora.Domain.Results.Result<Animora.Persistence.Sqlite.Maintenance.LocalStoreDiagnostics> Result,
        IReadOnlyList<string> Steps)> InitializeWithStepsAsync(CancellationToken ct = default)
    {
        using var scope = CreateScope();
        var startup = scope.ServiceProvider.GetRequiredService<Animora.Infrastructure.Local.Startup.LocalStoreStartup>();
        var result = await startup.InitializeAsync(ct);
        return (result, startup.Steps);
    }

    /// <summary>A context acting as <paramref name="tenantId"/> over the same file (for the tenant-guard tests).</summary>
    public AnimoraLocalDbContext CreateContext(string tenantId)
    {
        var options = new DbContextOptionsBuilder<AnimoraLocalDbContext>()
            .UseSqlite($"Data Source={DatabasePath}")
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .Options;
        return new AnimoraLocalDbContext(options, new FixedTenantContext(tenantId), Clock);
    }

    /// <summary>A context whose commands are counted, over the same file — for one-statement assertions.</summary>
    public (AnimoraLocalDbContext Context, CommandCounter Counter) CreateCountingContext()
    {
        var counter = new CommandCounter();
        var options = new DbContextOptionsBuilder<AnimoraLocalDbContext>()
            .UseSqlite($"Data Source={DatabasePath}")
            .AddInterceptors(counter)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .Options;
        return (new AnimoraLocalDbContext(options, new FixedTenantContext(TenantId), Clock), counter);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _services.Dispose();
        SqliteConnection.ClearAllPools();
        TryDelete(DatabasePath);
        TryDelete(DatabasePath + "-wal");
        TryDelete(DatabasePath + "-shm");
        try
        {
            System.IO.Directory.Delete(Directory, recursive: true);
        }
        catch (IOException)
        {
            // A leftover file is not a test failure; the OS reclaims the temp dir.
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
