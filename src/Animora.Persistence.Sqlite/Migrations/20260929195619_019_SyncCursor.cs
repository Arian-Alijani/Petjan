using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Animora.Persistence.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class _019_SyncCursor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SyncCursors",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    Scope = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    LastSyncedAtUtc = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncCursors", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SyncCursors_TenantId_Scope",
                table: "SyncCursors",
                columns: new[] { "TenantId", "Scope" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SyncCursors");
        }
    }
}
