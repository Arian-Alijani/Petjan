using Animora.Persistence.PostgreSql.Context;
using Animora.Persistence.PostgreSql.Replication;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Animora.Persistence.PostgreSql.Migrations.Server;

/// <summary>Allow root concurrency checks to finish before child triggers rotate root tokens.</summary>
[DbContext(typeof(AnimoraServerDbContext))]
[Migration("20261019140000_120_AggregateChildConcurrency")]
public sealed class _120_AggregateChildConcurrency : Migration
{
    // Freeze this migration's physical surface: future catalog additions must own their DDL.
    private static readonly SyncEntityDescriptor[] Aggregates =
    [
        new("Product", typeof(Animora.Domain.Inventory.Product), "Products", [new("ProductVariants", "OwnerId", false)]),
        new("BoardingStay", typeof(Animora.Domain.Boarding.BoardingStay), "BoardingStays",
            [new("BoardingStayItems", "OwnerId", false), new("BoardingHandoffLines", "StayId", true)]),
        new("Visit", typeof(Animora.Domain.Clinical.Visit), "Visits",
            [new("VisitRxLines", "OwnerId", false), new("VisitHandoffLines", "VisitId", true)]),
    ];

    protected override void Up(MigrationBuilder migrationBuilder) => Replace(migrationBuilder, deferrable: true);
    protected override void Down(MigrationBuilder migrationBuilder) => Replace(migrationBuilder, deferrable: false);

    private static void Replace(MigrationBuilder builder, bool deferrable)
    {
        foreach (var descriptor in Aggregates)
        foreach (var child in descriptor.Children)
        {
            builder.Sql($"DROP TRIGGER {JournalSqlTemplates.Quote("trg_" + child.Table + "_journal")} ON public.{JournalSqlTemplates.Quote(child.Table)};");
            builder.Sql(deferrable ? JournalSqlTemplates.DeferrableChildCaptureTrigger(descriptor, child)
                : JournalSqlTemplates.CaptureTrigger(descriptor, child.Table, child.RootIdColumn, child: true));
        }
    }
}
