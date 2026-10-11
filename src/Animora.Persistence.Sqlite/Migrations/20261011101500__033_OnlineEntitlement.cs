using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Animora.Persistence.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class _033_OnlineEntitlement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClientSessions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    DeviceId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    AccessToken = table.Column<string>(type: "TEXT", maxLength: 8192, nullable: false),
                    AccessExpiresUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    RefreshToken = table.Column<string>(type: "TEXT", maxLength: 8192, nullable: false),
                    Role = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    SecurityStamp = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    OfflineCredentialVerifier = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    UpdatedUtc = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SealedEntitlements",
                columns: table => new
                {
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    LicenseToken = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false),
                    HighWaterUnixMs = table.Column<long>(type: "INTEGER", nullable: false),
                    HighestSequenceSeen = table.Column<long>(type: "INTEGER", nullable: false),
                    InstallationIdHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    WrittenUnixMs = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SealedEntitlements", x => x.TenantId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClientSessions_TenantId_UserId_DeviceId",
                table: "ClientSessions",
                columns: new[] { "TenantId", "UserId", "DeviceId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClientSessions");

            migrationBuilder.DropTable(
                name: "SealedEntitlements");
        }
    }
}
