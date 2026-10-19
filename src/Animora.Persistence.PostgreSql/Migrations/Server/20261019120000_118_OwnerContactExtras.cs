using Animora.Persistence.PostgreSql.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Animora.Persistence.PostgreSql.Migrations.Server;

/// <summary>Add the owner's optional landline phone and free-form referral note (existing owners default to NULL).</summary>
[DbContext(typeof(AnimoraServerDbContext))]
[Migration("20261019120000_118_OwnerContactExtras")]
public sealed class _118_OwnerContactExtras : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE public."Owners" ADD COLUMN "PhoneLandline" character varying(40) NULL;
        ALTER TABLE public."Owners" ADD COLUMN "Referral" character varying(200) NULL;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE public."Owners" DROP COLUMN "Referral";
        ALTER TABLE public."Owners" DROP COLUMN "PhoneLandline";
        """);
}
