using Animora.Persistence.PostgreSql.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Animora.Persistence.PostgreSql.Migrations.Server;

/// <summary>
/// Adds the pet-profile extras the info tab round-trips (habitat / behavior / diet / blood type /
/// insurance), the microchip vendor + implant location and the neutering date. These columns were
/// added to the local SQLite store by the 037_PetProfileExtras migration but never to the server,
/// so the <c>Pet</c> business projection failed with <c>42703: column "Behavior" of relation "Pets"
/// does not exist</c> on apply (which then cascaded into dependent commands such as weight entries).
/// All columns are nullable <c>text</c> — matching the convention of every other free-text Pet
/// column — so existing rows default to "not recorded".
/// </summary>
[DbContext(typeof(AnimoraServerDbContext))]
[Migration("20261019130000_119_PetProfileExtras")]
public sealed class _119_PetProfileExtras : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE public."Pets" ADD COLUMN "MicrochipVendor" text NULL;
        ALTER TABLE public."Pets" ADD COLUMN "MicrochipLocation" text NULL;
        ALTER TABLE public."Pets" ADD COLUMN "Habitat" text NULL;
        ALTER TABLE public."Pets" ADD COLUMN "Behavior" text NULL;
        ALTER TABLE public."Pets" ADD COLUMN "Diet" text NULL;
        ALTER TABLE public."Pets" ADD COLUMN "BloodType" text NULL;
        ALTER TABLE public."Pets" ADD COLUMN "Insurance" text NULL;
        ALTER TABLE public."Pets" ADD COLUMN "NeuteredUtc" timestamp with time zone NULL;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE public."Pets" DROP COLUMN "NeuteredUtc";
        ALTER TABLE public."Pets" DROP COLUMN "Insurance";
        ALTER TABLE public."Pets" DROP COLUMN "BloodType";
        ALTER TABLE public."Pets" DROP COLUMN "Diet";
        ALTER TABLE public."Pets" DROP COLUMN "Behavior";
        ALTER TABLE public."Pets" DROP COLUMN "Habitat";
        ALTER TABLE public."Pets" DROP COLUMN "MicrochipLocation";
        ALTER TABLE public."Pets" DROP COLUMN "MicrochipVendor";
        """);
}
