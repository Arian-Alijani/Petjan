using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Animora.Persistence.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class _022_DashboardWidgetIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Visits_TenantId_DoctorUserId_CompletedUtc",
                table: "Visits",
                columns: new[] { "TenantId", "DoctorUserId", "CompletedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_VaccinationRecords_TenantId_AdministeredUtc",
                table: "VaccinationRecords",
                columns: new[] { "TenantId", "AdministeredUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ReceptionQueueEntries_TenantId_AppointmentId",
                table: "ReceptionQueueEntries",
                columns: new[] { "TenantId", "AppointmentId" },
                filter: "\"AppointmentId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MedicalHistoryEntries_TenantId_SourceEntityId",
                table: "MedicalHistoryEntries",
                columns: new[] { "TenantId", "SourceEntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityEvents_TenantId_LocalDay_Id",
                table: "ActivityEvents",
                columns: new[] { "TenantId", "LocalDay", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Visits_TenantId_DoctorUserId_CompletedUtc",
                table: "Visits");

            migrationBuilder.DropIndex(
                name: "IX_VaccinationRecords_TenantId_AdministeredUtc",
                table: "VaccinationRecords");

            migrationBuilder.DropIndex(
                name: "IX_ReceptionQueueEntries_TenantId_AppointmentId",
                table: "ReceptionQueueEntries");

            migrationBuilder.DropIndex(
                name: "IX_MedicalHistoryEntries_TenantId_SourceEntityId",
                table: "MedicalHistoryEntries");

            migrationBuilder.DropIndex(
                name: "IX_ActivityEvents_TenantId_LocalDay_Id",
                table: "ActivityEvents");
        }
    }
}
