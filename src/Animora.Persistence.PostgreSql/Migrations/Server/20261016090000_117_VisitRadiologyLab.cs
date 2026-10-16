using Animora.Persistence.PostgreSql.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Animora.Persistence.PostgreSql.Migrations.Server;

/// <summary>Add the visit's radiology and lab-result jsonb lists (existing visits default to an empty list).</summary>
[DbContext(typeof(AnimoraServerDbContext))]
[Migration("20261016090000_117_VisitRadiologyLab")]
public sealed class _117_VisitRadiologyLab : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE public."Visits" ADD COLUMN "Radiology" jsonb NOT NULL DEFAULT '[]'::jsonb;
        ALTER TABLE public."Visits" ADD COLUMN "LabResults" jsonb NOT NULL DEFAULT '[]'::jsonb;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE public."Visits" DROP COLUMN "LabResults";
        ALTER TABLE public."Visits" DROP COLUMN "Radiology";
        """);
}
