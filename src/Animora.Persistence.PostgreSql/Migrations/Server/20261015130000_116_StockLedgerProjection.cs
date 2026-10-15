using Animora.Persistence.PostgreSql.Context;
using Animora.Persistence.PostgreSql.Replication;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Animora.Persistence.PostgreSql.Migrations.Server;

/// <summary>Capture server-settled stock and force one snapshot to backfill existing stock on every client.</summary>
[DbContext(typeof(AnimoraServerDbContext))]
[Migration("20261015130000_116_StockLedgerProjection")]
public sealed class StockLedgerProjection : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        JournalTriggers.Apply(migrationBuilder,
            new SyncEntityDescriptor("StockLedgerEntry", typeof(Animora.Domain.Inventory.StockMovement), "StockMovements", []));
        migrationBuilder.Sql("""
            UPDATE public."TenantFeedState" SET "Epoch" = "Epoch" + 1, "Generation" = "Generation" + 1,
                "OldestRetainedSequence" = "LastSequence", "UpdatedUtc" = clock_timestamp();
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        JournalTriggers.Drop(migrationBuilder,
            new SyncEntityDescriptor("StockLedgerEntry", typeof(Animora.Domain.Inventory.StockMovement), "StockMovements", []));
        migrationBuilder.Sql("""
            UPDATE public."TenantFeedState" SET "Epoch" = "Epoch" + 1, "Generation" = "Generation" + 1,
                "OldestRetainedSequence" = "LastSequence", "UpdatedUtc" = clock_timestamp();
            """);
    }
}
