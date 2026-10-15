using Animora.Persistence.Sqlite.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Animora.Persistence.Sqlite.Migrations;

/// <summary>Add full peer invoice detail and replay existing summaries once to backfill it.</summary>
[DbContext(typeof(AnimoraLocalDbContext))]
[Migration("20261015120000__035_PeerInvoiceDetail")]
public sealed class PeerInvoiceDetail : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "DetailJson", table: "PeerInvoices",
            type: "TEXT", maxLength: 400, nullable: true);
        migrationBuilder.Sql("UPDATE PeerFinanceCursors SET CursorTicks = 0;");
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(name: "DetailJson", table: "PeerInvoices");
}
