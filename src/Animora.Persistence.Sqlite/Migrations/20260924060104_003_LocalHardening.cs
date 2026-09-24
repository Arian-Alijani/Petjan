using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Animora.Persistence.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class _003_LocalHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClockHighWaters",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    HighWaterUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAtUtc = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClockHighWaters", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LocalInstallations",
                columns: table => new
                {
                    InstallationId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    MachineFingerprintHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    SchemaVersion = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LocalInstallations", x => x.InstallationId);
                });

            migrationBuilder.CreateTable(
                name: "StoreMaintenanceLogs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    OccurredAtUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    Detail = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoreMaintenanceLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StoreMaintenanceLogs_OccurredAtUtc",
                table: "StoreMaintenanceLogs",
                column: "OccurredAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClockHighWaters");

            migrationBuilder.DropTable(
                name: "LocalInstallations");

            migrationBuilder.DropTable(
                name: "StoreMaintenanceLogs");
        }
    }
}
