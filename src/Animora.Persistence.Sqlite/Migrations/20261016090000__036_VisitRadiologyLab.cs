using Animora.Persistence.Sqlite.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Animora.Persistence.Sqlite.Migrations;

/// <summary>Add the visit's radiology and lab-result JSON lists (existing visits default to an empty list).</summary>
[DbContext(typeof(AnimoraLocalDbContext))]
[Migration("20261016090000__036_VisitRadiologyLab")]
public sealed class VisitRadiologyLab : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "Radiology", table: "Visits",
            type: "TEXT", nullable: false, defaultValue: "[]");
        migrationBuilder.AddColumn<string>(name: "LabResults", table: "Visits",
            type: "TEXT", nullable: false, defaultValue: "[]");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "LabResults", table: "Visits");
        migrationBuilder.DropColumn(name: "Radiology", table: "Visits");
    }
}
