using Animora.Persistence.Sqlite.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Animora.Persistence.Sqlite.Migrations;

/// <summary>
/// Adds the pet-profile extras that the info tab must round-trip: habitat / behavior / diet /
/// blood type / insurance, the microchip vendor + implant location, and the neutering date.
/// All columns are nullable so existing rows default to "not recorded".
/// </summary>
[DbContext(typeof(AnimoraLocalDbContext))]
[Migration("20261018120000__037_PetProfileExtras")]
public sealed class PetProfileExtras : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "Habitat", table: "Pets",
            type: "TEXT", maxLength: 400, nullable: true);
        migrationBuilder.AddColumn<string>(name: "Behavior", table: "Pets",
            type: "TEXT", maxLength: 400, nullable: true);
        migrationBuilder.AddColumn<string>(name: "Diet", table: "Pets",
            type: "TEXT", maxLength: 400, nullable: true);
        migrationBuilder.AddColumn<string>(name: "BloodType", table: "Pets",
            type: "TEXT", maxLength: 400, nullable: true);
        migrationBuilder.AddColumn<string>(name: "Insurance", table: "Pets",
            type: "TEXT", maxLength: 400, nullable: true);
        migrationBuilder.AddColumn<string>(name: "MicrochipVendor", table: "Pets",
            type: "TEXT", maxLength: 400, nullable: true);
        migrationBuilder.AddColumn<string>(name: "MicrochipLocation", table: "Pets",
            type: "TEXT", maxLength: 400, nullable: true);
        migrationBuilder.AddColumn<long>(name: "NeuteredUtc", table: "Pets",
            type: "INTEGER", nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "Habitat", table: "Pets");
        migrationBuilder.DropColumn(name: "Behavior", table: "Pets");
        migrationBuilder.DropColumn(name: "Diet", table: "Pets");
        migrationBuilder.DropColumn(name: "BloodType", table: "Pets");
        migrationBuilder.DropColumn(name: "Insurance", table: "Pets");
        migrationBuilder.DropColumn(name: "MicrochipVendor", table: "Pets");
        migrationBuilder.DropColumn(name: "MicrochipLocation", table: "Pets");
        migrationBuilder.DropColumn(name: "NeuteredUtc", table: "Pets");
    }
}
