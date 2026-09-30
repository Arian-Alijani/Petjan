using Animora.Application.Ports;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Entities;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.Sqlite.Sync;

public sealed class SyncCursorStore(AnimoraLocalDbContext db, ILocalTenantContext tenant) : ISyncCursorStore
{
    public Task<DateTimeOffset?> ReadAsync(CancellationToken ct = default) => db.Set<SyncCursor>()
        .Where(c => c.TenantId == tenant.TenantId && c.Scope == SyncCursor.DefaultScope)
        .Select(c => c.LastSyncedAtUtc).SingleOrDefaultAsync(ct);

    public async Task MarkSyncedAsync(DateTimeOffset whenUtc, CancellationToken ct = default)
    {
        // Atomic upsert: two scopes cannot create duplicate cursors or move the marker backwards.
        var id = Animora.Domain.Primitives.IdGenerator.NewId();
        var tenantId = tenant.TenantId;
        var scope = SyncCursor.DefaultScope;
        var ticks = whenUtc.UtcTicks;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO SyncCursors (Id, TenantId, Scope, LastSyncedAtUtc)
            VALUES ({id}, {tenantId}, {scope}, {ticks})
            ON CONFLICT(TenantId, Scope) DO UPDATE SET LastSyncedAtUtc = excluded.LastSyncedAtUtc
            WHERE SyncCursors.LastSyncedAtUtc IS NULL OR SyncCursors.LastSyncedAtUtc < excluded.LastSyncedAtUtc
            """, ct);
    }
}
