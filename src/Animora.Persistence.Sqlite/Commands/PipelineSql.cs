using System.Data;
using System.Globalization;
using Animora.Domain.Primitives;
using Animora.Persistence.Sqlite.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Animora.Persistence.Sqlite.Commands;

/// <summary>
/// The raw-SQL building blocks of the command pipeline: the single-statement monotonic sequence allocation
/// and the single-statement atomic claim. Both are deliberately one SQL command so SQLite's own locking — not
/// application logic — arbitrates between competing callers (ARCHITECTURE.md §12.1).
/// </summary>
internal static class PipelineSql
{
    /// <summary>
    /// Atomically reserves the next sequence value: an upsert that inserts <c>1</c> on first use and otherwise
    /// increments, returning the new value — one statement, no read-modify-write race.
    /// </summary>
    internal static async Task<long> NextSequenceAsync(AnimoraLocalDbContext db, string name, CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();
        var opened = connection.State != ConnectionState.Open;
        if (opened) await db.Database.OpenConnectionAsync(ct).ConfigureAwait(false);
        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
            command.CommandText =
                "INSERT INTO LocalSequenceCounters (Name, Value) VALUES ($name, 1) " +
                "ON CONFLICT(Name) DO UPDATE SET Value = Value + 1 RETURNING Value;";
            AddParameter(command, "$name", name);
            var scalar = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
            return Convert.ToInt64(scalar, CultureInfo.InvariantCulture);
        }
        finally
        {
            if (opened) await db.Database.CloseConnectionAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The atomic command claim: one conditional <c>UPDATE</c> takes up to <paramref name="max"/> eligible rows
    /// (ordered by <c>LocalSequence</c>) under <paramref name="token"/> and a lease ending at
    /// <paramref name="until"/>. An expired lease is reclaimable, so a crashed processor never strands work.
    /// </summary>
    internal static Task<int> ClaimPendingCommandsAsync(AnimoraLocalDbContext db, string token,
        DateTimeOffset now, DateTimeOffset until, int max, CancellationToken ct)
    {
        var locallyPosted = (int)SettlementState.LocallyPosted;
        var pending = (int)SettlementState.PendingSettlement;
        // Time columns are stored as UTC ticks (UtcTicksConverter); the comparison must use ticks, not ISO text.
        return db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE PendingCommands
            SET ClaimToken = {token}, ClaimedUntilUtc = {until.UtcTicks}, Status = {pending}
            WHERE Id IN (
                SELECT Id FROM PendingCommands
                WHERE Status IN ({locallyPosted}, {pending})
                  AND (ClaimedUntilUtc IS NULL OR ClaimedUntilUtc < {now.UtcTicks})
                  AND (NextAttemptUtc IS NULL OR NextAttemptUtc <= {now.UtcTicks})
                ORDER BY LocalSequence
                LIMIT {max})
            """, ct);
    }

    /// <summary>The atomic outbox claim — the same single-statement shape as <see cref="ClaimPendingCommandsAsync"/>.</summary>
    internal static Task<int> ClaimOutboxAsync(AnimoraLocalDbContext db, string token,
        DateTimeOffset now, DateTimeOffset until, int max, CancellationToken ct)
    {
        const int pending = (int)Entities.OutboxStatus.Pending;
        const int retry = (int)Entities.OutboxStatus.Retry;
        return db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE OutboxMessages
            SET ClaimToken = {token}, ClaimedUntilUtc = {until.UtcTicks}, Status = {(int)Entities.OutboxStatus.Claimed}
            WHERE Id IN (
                SELECT Id FROM OutboxMessages
                WHERE Status IN ({pending}, {retry})
                  AND (ClaimedUntilUtc IS NULL OR ClaimedUntilUtc < {now.UtcTicks})
                  AND (NextAttemptUtc IS NULL OR NextAttemptUtc <= {now.UtcTicks})
                ORDER BY CreatedAtUtc, Id
                LIMIT {max})
            """, ct);
    }

    private static void AddParameter(System.Data.Common.DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
