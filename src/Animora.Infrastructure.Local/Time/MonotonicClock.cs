using System.Diagnostics;
using Animora.Domain.Time;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Animora.Infrastructure.Local.Time;

/// <summary>
/// A-RULE 011: rollback-resistant clock. <see cref="HighWaterUtc"/> is loaded lazily from the single
/// <see cref="ClockHighWater"/> row and only ever moves forward, in memory and on disk (conditional upsert).
/// Singleton; the context factory is scoped, so each DB access uses its own short-lived scope + context.
/// </summary>
public sealed class MonotonicClock(IServiceScopeFactory scopes, IClock clock) : IMonotonicClock
{
    // Single statement: insert if missing, otherwise update only when strictly greater.
    private const string UpsertSql =
        "INSERT INTO ClockHighWaters (Id, HighWaterUtc, UpdatedAtUtc) VALUES ({0}, {1}, {2}) " +
        "ON CONFLICT(Id) DO UPDATE SET HighWaterUtc = excluded.HighWaterUtc, UpdatedAtUtc = excluded.UpdatedAtUtc " +
        "WHERE ClockHighWaters.HighWaterUtc < excluded.HighWaterUtc;";

    private readonly Lock _gate = new();
    private DateTimeOffset _highWater = DateTimeOffset.MinValue;
    private bool _loaded;

    public long ElapsedTicks => Stopwatch.GetTimestamp();

    public DateTimeOffset HighWaterUtc
    {
        get
        {
            lock (_gate)
            {
                EnsureLoaded();
                return _highWater;
            }
        }
    }

    public void Observe(DateTimeOffset serverUtc)
    {
        var value = serverUtc.ToUniversalTime();
        lock (_gate)
        {
            EnsureLoaded();
            if (value <= _highWater) return;
            _highWater = value;
            Persist(value);
        }
    }

    private void EnsureLoaded()
    {
        if (_loaded) return;
        try
        {
            using var scope = scopes.CreateScope();
            using var db = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AnimoraLocalDbContext>>()
                .CreateDbContext();
            var stored = db.Set<ClockHighWater>().AsNoTracking()
                .Where(e => e.Id == ClockHighWater.SingletonId)
                .Select(e => (DateTimeOffset?)e.HighWaterUtc)
                .FirstOrDefault();
            if (stored is { } persisted && persisted > _highWater) _highWater = persisted;
            _loaded = true;
        }
        catch (Exception ex) when (IsStoreFailure(ex))
        {
            // Store not ready yet (pre-migration / key unavailable): keep the in-memory value, retry next read.
        }
    }

    private void Persist(DateTimeOffset value)
    {
        try
        {
            using var scope = scopes.CreateScope();
            using var db = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AnimoraLocalDbContext>>()
                .CreateDbContext();
            db.Database.ExecuteSqlRaw(UpsertSql, ClockHighWater.SingletonId, value.UtcTicks, clock.UtcNow.UtcTicks);
        }
        catch (Exception ex) when (IsStoreFailure(ex))
        {
            // Memory already advanced; the next successful Observe persists a value >= this one.
        }
    }

    private static bool IsStoreFailure(Exception ex) =>
        ex is SqliteException or InvalidOperationException or IOException or UnauthorizedAccessException;
}
