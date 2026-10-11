using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Animora.Persistence.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class _032_ReplicaStore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "repl_base",
                columns: table => new
                {
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    Generation = table.Column<long>(type: "INTEGER", nullable: false),
                    Entity = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    EntityId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Version = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Payload = table.Column<string>(type: "TEXT", maxLength: 262144, nullable: true),
                    Deleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    UpdatedAtUtc = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_repl_base", x => new { x.TenantId, x.Generation, x.Entity, x.EntityId });
                });

            migrationBuilder.CreateTable(
                name: "repl_checkpoint",
                columns: table => new
                {
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    Scope = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Epoch = table.Column<long>(type: "INTEGER", nullable: false),
                    Watermark = table.Column<long>(type: "INTEGER", nullable: false),
                    Generation = table.Column<long>(type: "INTEGER", nullable: false),
                    ServerGeneration = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_repl_checkpoint", x => new { x.TenantId, x.Scope });
                });

            migrationBuilder.CreateTable(
                name: "repl_staging",
                columns: table => new
                {
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    BatchId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Entity = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    EntityId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    PageOrdinal = table.Column<int>(type: "INTEGER", nullable: false),
                    Version = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Payload = table.Column<string>(type: "TEXT", maxLength: 262144, nullable: true),
                    Deleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    UpdatedAtUtc = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_repl_staging", x => new { x.TenantId, x.BatchId, x.Entity, x.EntityId });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "repl_base");

            migrationBuilder.DropTable(
                name: "repl_checkpoint");

            migrationBuilder.DropTable(
                name: "repl_staging");
        }
    }
}
