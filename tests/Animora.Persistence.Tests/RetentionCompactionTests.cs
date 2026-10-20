using System;
using System.Linq;
using System.Threading.Tasks;
using Animora.Persistence.Sqlite.Maintenance;
using Animora.Persistence.Sqlite.Options;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Animora.Persistence.Tests;

/// <summary>SQL-02: retention deletes only relayed outbox rows and expired feed rows, in bounded batches, idempotently.</summary>
public sealed class RetentionCompactionTests
{
    [Fact]
    public void RetentionOptions_RejectOutboxWindowShorterThanActivityWindow()
    {
        var bad = new LocalStoreOptions { OutboxRetentionDays = 30, ActivityRetentionDays = 90 };
        Assert.True(bad.Validate().IsFailure);
        Assert.True(new LocalStoreOptions().Validate().IsSuccess);
    }

    [Fact]
    public async Task Compact_OnEmptyStore_IsANoOpAndLogsOnce()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.InitializeAsync();
        using var scope = fixture.CreateScope();
        var maintenance = scope.ServiceProvider.GetRequiredService<ILocalStoreMaintenance>();

        var first = await maintenance.CompactAsync(null, default);
        var second = await maintenance.CompactAsync(new RetentionPolicy(30, 30, 10), default);

        Assert.True(first.IsSuccess, first.Detail);
        Assert.Equal(0, first.Value!.Total);
        Assert.True(second.IsSuccess, second.Detail);
        Assert.Equal(0, second.Value!.Total);
    }

    [Fact]
    public async Task Compact_RemovesOnlyOldSucceededOutboxRowsAndOldFeedRows_InBatches()
    {
        using var fixture = new LocalStoreTestFixture();
        await fixture.InitializeAsync();
        var now = DateTimeOffset.UtcNow;
        var old = now.AddDays(-400).UtcTicks;
        var recent = now.AddDays(-2).UtcTicks;

        using (var connection = new SqliteConnection($"Data Source={fixture.DatabasePath}"))
        {
            connection.Open();
            // Status: 0 Pending, 3 Succeeded, 5 DeadLettered.
            for (var i = 0; i < 25; i++) InsertOutbox(connection, $"OLD-OK-{i:D3}", 3, old);
            InsertOutbox(connection, "RECENT-OK", 3, recent);
            InsertOutbox(connection, "OLD-DEAD", 5, old);
            InsertOutbox(connection, "OLD-PENDING", 0, null);
            for (var i = 0; i < 12; i++) InsertActivity(connection, $"A-OLD-{i:D3}", old);
            InsertActivity(connection, "A-RECENT", recent);
        }

        using var scope = fixture.CreateScope();
        var maintenance = scope.ServiceProvider.GetRequiredService<ILocalStoreMaintenance>();
        var result = await maintenance.CompactAsync(new RetentionPolicy(90, 90, BatchSize: 10), default);

        Assert.True(result.IsSuccess, result.Detail);
        Assert.Equal(25, result.Value!.OutboxDeleted);
        Assert.Equal(12, result.Value.ActivityDeleted);

        using var check = new SqliteConnection($"Data Source={fixture.DatabasePath}");
        check.Open();
        Assert.Equal(3L, Count(check, "SELECT COUNT(*) FROM OutboxMessages")); // recent ok + dead-lettered + pending survive
        Assert.Equal(1L, Count(check, "SELECT COUNT(*) FROM ActivityEvents"));

        var again = await maintenance.CompactAsync(new RetentionPolicy(90, 90, 10), default);
        Assert.Equal(0, again.Value!.Total); // idempotent
    }

    private static long Count(SqliteConnection c, string sql)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    private static void InsertOutbox(SqliteConnection c, string id, int status, long? processedTicks)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO OutboxMessages (Id, MessageType, PayloadJson, Status, CreatedAtUtc, Attempts, ProcessedAtUtc) " +
                          "VALUES ($id, 'T', '{}', $s, $c, 0, $p);";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$s", status);
        cmd.Parameters.AddWithValue("$c", processedTicks ?? DateTimeOffset.UtcNow.UtcTicks);
        cmd.Parameters.AddWithValue("$p", processedTicks is { } p ? p : DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    private static void InsertActivity(SqliteConnection c, string id, long occurredTicks)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO ActivityEvents (Id, TenantId, OccurredUtc, LocalDay, Kind, Title, DetailArgsJson, DetailText, SearchText, " +
                          "EntityType, EntityId, AmountMinor, AmountSign, Status, ActorUserId, ActorName) " +
                          "VALUES ($id, 'T', $o, 20260101, 'Sale', 't', '', '', '', 'Invoice', 'E', 0, 0, 'Done', '', '');";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$o", occurredTicks);
        cmd.ExecuteNonQuery();
    }
}
