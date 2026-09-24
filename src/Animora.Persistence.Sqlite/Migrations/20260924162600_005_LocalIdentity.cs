using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Animora.Persistence.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class _005_LocalIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "MustChangePassword",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "LocalSessions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    DeviceId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    StartedAtUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    LastActivityUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    ExpiresAtUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    IsLocked = table.Column<bool>(type: "INTEGER", nullable: false),
                    EndedAtUtc = table.Column<long>(type: "INTEGER", nullable: true),
                    EndReason = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LocalSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LoginAttemptLogs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    UsernameAttempted = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    Outcome = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    OccurredAtUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    DeviceId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoginAttemptLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LocalSessions_DeviceId_Active",
                table: "LocalSessions",
                column: "DeviceId",
                unique: true,
                filter: "\"EndedAtUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_LoginAttemptLogs_OccurredAtUtc",
                table: "LoginAttemptLogs",
                column: "OccurredAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LocalSessions");

            migrationBuilder.DropTable(
                name: "LoginAttemptLogs");

            migrationBuilder.DropColumn(
                name: "MustChangePassword",
                table: "Users");
        }
    }
}
