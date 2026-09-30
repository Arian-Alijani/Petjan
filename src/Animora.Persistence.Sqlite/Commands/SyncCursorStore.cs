using Animora.Application.Ports;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Entities;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.Sqlite.Commands;

/// <summary>Explicit tenant predicate: LocalOnly rows are not covered by the business tenant filter.</summary>
public sealed class SyncCursorStore(AnimoraLocalDbContext db, ILocalTenantContext tenant) : ISyncCursorStore
{
    public Task<DateTimeOffset?> ReadAsync(CancellationToken ct = default) => db.Set<SyncCursor>()
        .Where(x => x.TenantId == tenant.TenantId && x.Scope == SyncCursor.DefaultScope)
        .Select(x => x.LastSyncedAtUtc).SingleOrDefaultAsync(ct);

    public async Task MarkSyncedAsync(DateTimeOffset whenUtc, CancellationToken ct = default)
    {
        // Atomic upsert avoids duplicate rows across scopes/processes and never lowers the watermark.
        var id = Animora.Domain.Primitives.IdGenerator.NewId();
        var ticks = whenUtc.UtcTicks;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "SyncCursors" ("Id", "TenantId", "Scope", "LastSyncedAtUtc")
            VALUES ({id}, {tenant.TenantId}, {SyncCursor.DefaultScope}, {ticks})
            ON CONFLICT ("TenantId", "Scope") DO UPDATE SET "LastSyncedAtUtc" = excluded."LastSyncedAtUtc"
            WHERE "SyncCursors"."LastSyncedAtUtc" IS NULL OR "SyncCursors"."LastSyncedAtUtc" < excluded."LastSyncedAtUtc"
            """, ct);
    }
}
