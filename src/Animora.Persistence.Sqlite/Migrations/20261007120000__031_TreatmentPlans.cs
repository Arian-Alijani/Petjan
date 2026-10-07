using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Animora.Persistence.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class _031_TreatmentPlans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TreatmentPlans",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    PetId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    ItemType = table.Column<int>(type: "INTEGER", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    VaccineCode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    Dosage = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    NotifyOwner = table.Column<bool>(type: "INTEGER", nullable: false),
                    ScheduleType = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    CreatedUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    Version = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    Deleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TreatmentPlans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TreatmentOccurrences",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    PlanId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    DueDay = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    DoneUtc = table.Column<long>(type: "INTEGER", nullable: true),
                    DoneByUserId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: true),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    Version = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    Deleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TreatmentOccurrences", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VaccineCatalogItems",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Protects = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    TotalDoses = table.Column<int>(type: "INTEGER", nullable: false),
                    BoosterDays = table.Column<int>(type: "INTEGER", nullable: false),
                    Stock = table.Column<int>(type: "INTEGER", nullable: false),
                    MinStock = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    Version = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    Deleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VaccineCatalogItems", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TreatmentPlans_TenantId_UpdatedAt",
                table: "TreatmentPlans",
                columns: new[] { "TenantId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TreatmentPlans_TenantId_PetId_State",
                table: "TreatmentPlans",
                columns: new[] { "TenantId", "PetId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_TreatmentOccurrences_TenantId_UpdatedAt",
                table: "TreatmentOccurrences",
                columns: new[] { "TenantId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TreatmentOccurrences_TenantId_DueDay_State",
                table: "TreatmentOccurrences",
                columns: new[] { "TenantId", "DueDay", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_TreatmentOccurrences_TenantId_PlanId",
                table: "TreatmentOccurrences",
                columns: new[] { "TenantId", "PlanId" });

            migrationBuilder.CreateIndex(
                name: "IX_VaccineCatalogItems_TenantId_UpdatedAt",
                table: "VaccineCatalogItems",
                columns: new[] { "TenantId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_VaccineCatalogItems_TenantId_Code",
                table: "VaccineCatalogItems",
                columns: new[] { "TenantId", "Code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TreatmentOccurrences");

            migrationBuilder.DropTable(
                name: "TreatmentPlans");

            migrationBuilder.DropTable(
                name: "VaccineCatalogItems");
        }
    }
}
