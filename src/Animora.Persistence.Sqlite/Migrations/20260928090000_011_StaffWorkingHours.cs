using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Animora.Persistence.Sqlite.Migrations;

public partial class _011_StaffWorkingHours : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>("WorkStartMinute", "StaffMemberships", type: "INTEGER", nullable: false, defaultValue: 480);
        migrationBuilder.AddColumn<int>("WorkEndMinute", "StaffMemberships", type: "INTEGER", nullable: false, defaultValue: 1200);
        migrationBuilder.AddColumn<int>("WorkDaysMask", "StaffMemberships", type: "INTEGER", nullable: false, defaultValue: 63);
        migrationBuilder.CreateIndex("IX_Appointments_TenantId_DoctorId_StartUtc", "Appointments",
            new[] { "TenantId", "DoctorUserId", "StartUtc" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex("IX_Appointments_TenantId_DoctorId_StartUtc", "Appointments");
        migrationBuilder.DropColumn("WorkStartMinute", "StaffMemberships");
        migrationBuilder.DropColumn("WorkEndMinute", "StaffMemberships");
        migrationBuilder.DropColumn("WorkDaysMask", "StaffMemberships");
    }
}
