using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Animora.Persistence.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class _017_ActivityFeed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ActivityEvents",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    OccurredUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    LocalDay = table.Column<int>(type: "INTEGER", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    DetailArgsJson = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    EntityType = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    EntityId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    AmountMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    AmountSign = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    ActorUserId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    ActorName = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    CorrelationId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityEvents_TenantId_EntityType_EntityId",
                table: "ActivityEvents",
                columns: new[] { "TenantId", "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityEvents_TenantId_Kind_OccurredUtc",
                table: "ActivityEvents",
                columns: new[] { "TenantId", "Kind", "OccurredUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityEvents_TenantId_OccurredUtc",
                table: "ActivityEvents",
                columns: new[] { "TenantId", "OccurredUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActivityEvents");
        }
    }
}
