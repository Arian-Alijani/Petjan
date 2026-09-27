using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Animora.Persistence.Sqlite.Migrations;

public partial class _008_UiDraftState : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "UiDraftStates",
            columns: table => new
            {
                Id = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                ScreenKey = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                UserId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                PayloadJson = table.Column<string>(type: "TEXT", maxLength: 65536, nullable: false),
                UpdatedAtUtc = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_UiDraftStates", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_UiDraftStates_ScreenKey_UserId",
            table: "UiDraftStates",
            columns: new[] { "ScreenKey", "UserId" },
            unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_UiDraftStates_UpdatedAtUtc",
            table: "UiDraftStates",
            column: "UpdatedAtUtc");
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "UiDraftStates");
}
