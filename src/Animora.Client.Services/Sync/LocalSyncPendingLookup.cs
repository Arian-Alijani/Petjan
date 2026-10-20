using Animora.Contracts.Diagnostics;
using Animora.Contracts.Sync;
using Animora.Domain.Primitives;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Animora.Client.Services.Sync;

/// <summary>
/// Builds the <see cref="SyncQueueSnapshot"/> from the local command journal in ONE projection query (no entity
/// materialisation, no per-row work). A creation command has no aggregate id yet — its aggregate id IS its operation
/// id — so that is the key used for it. A terminal refusal (Rejected / Conflict / NeedsAttention) outranks a queued
/// write of the same record: the operator must see the problem, not the pending badge.
/// </summary>
public sealed class LocalSyncPendingLookup(IServiceScopeFactory scopes) : ISyncPendingLookup
{
    private static readonly SettlementState[] Queued =
        [SettlementState.Draft, SettlementState.LocallyPosted, SettlementState.PendingSettlement];

    private static readonly SettlementState[] Terminal =
        [SettlementState.Rejected, SettlementState.Conflict, SettlementState.NeedsAttention];

    public async Task<SyncQueueSnapshot> GetSnapshotAsync(CancellationToken ct = default)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
            var queued = Queued;
            var terminal = Terminal;
            var rows = await db.Set<PendingCommand>().AsNoTracking()
                .Where(c => queued.Contains(c.Status) || terminal.Contains(c.Status))
                .Select(c => new { c.AggregateType, c.AggregateId, c.OperationId, c.Status })
                .ToListAsync(ct).ConfigureAwait(false);

            var marks = new Dictionary<string, SyncQueueMark>(rows.Count, StringComparer.Ordinal);
            foreach (var row in rows)
            {
                var key = SyncQueueSnapshot.KeyOf(row.AggregateType, string.IsNullOrEmpty(row.AggregateId) ? row.OperationId : row.AggregateId);
                var mark = Array.IndexOf(Terminal, row.Status) >= 0 ? SyncQueueMark.NeedsAttention : SyncQueueMark.Queued;
                if (!marks.TryGetValue(key, out var existing) || mark > existing)
                {
                    marks[key] = mark;
                }
            }

            return new SyncQueueSnapshot(marks);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A history screen must still render when the journal cannot be read: no marks beats no screen.
            TraceLog.Warn($"[sync] queue index unavailable — {ex.Message}", "sync");
            return SyncQueueSnapshot.Empty;
        }
    }
}
