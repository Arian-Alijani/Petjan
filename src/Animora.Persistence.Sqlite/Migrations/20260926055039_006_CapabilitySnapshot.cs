using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Animora.Persistence.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class _006_CapabilitySnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DeviceCapabilitySnapshots",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    DeviceId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    PermissionsJson = table.Column<string>(type: "TEXT", maxLength: 8192, nullable: false),
                    IssuedAtUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    PerCapabilityExpiryJson = table.Column<string>(type: "TEXT", maxLength: 16384, nullable: false),
                    AbsoluteExpiryUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    SnapshotSignature = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SecurityStamp = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    RevokedAtUtc = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceCapabilitySnapshots", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeviceCapabilitySnapshots_Tenant_User_Device",
                table: "DeviceCapabilitySnapshots",
                columns: new[] { "TenantId", "UserId", "DeviceId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeviceCapabilitySnapshots");
        }
    }
}
