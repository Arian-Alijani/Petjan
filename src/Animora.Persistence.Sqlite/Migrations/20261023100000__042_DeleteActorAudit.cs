using Animora.Persistence.Sqlite.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Animora.Persistence.Sqlite.Migrations;

/// <summary>
/// Adds the soft-delete audit columns (who + when) to Owners and Pets so a deletion replicates with its actor
/// and instant and peer clients can show it in history. Both columns are nullable → additive, existing rows
/// default to "not deleted / not recorded".
/// </summary>
[DbContext(typeof(AnimoraLocalDbContext))]
[Migration("20261023100000__042_DeleteActorAudit")]
public sealed class DeleteActorAudit : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        foreach (var table in new[] { "Owners", "Pets" })
        {
            migrationBuilder.AddColumn<string>(name: "DeletedByUserId", table: table,
                type: "TEXT", maxLength: 400, nullable: true);
            migrationBuilder.AddColumn<long>(name: "DeletedUtc", table: table,
                type: "INTEGER", nullable: true);
        }
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        foreach (var table in new[] { "Owners", "Pets" })
        {
            migrationBuilder.DropColumn(name: "DeletedUtc", table: table);
            migrationBuilder.DropColumn(name: "DeletedByUserId", table: table);
        }
    }
}
