using Animora.Persistence.Sqlite.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Animora.Persistence.Sqlite.Migrations;

/// <summary>
/// SYN-01: durable provisional-id to server-id map so dependents of a settled creator still rewrite correctly after an app restart.
/// Purely additive (new local-only table); existing data is untouched.
/// </summary>
[DbContext(typeof(AnimoraLocalDbContext))]
[Migration("20261020120000__039_CommandIdRemap")]
public sealed class CommandIdRemapTable : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "CommandIdRemaps",
            columns: table => new
            {
                TenantId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                LocalId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                CreatedAtUtc = table.Column<long>(type: "INTEGER", nullable: false),
                ServerId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_CommandIdRemaps", x => new { x.TenantId, x.LocalId }));

        migrationBuilder.CreateIndex(name: "IX_CommandIdRemaps_CreatedAtUtc", table: "CommandIdRemaps", column: "CreatedAtUtc");
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "CommandIdRemaps");
}
