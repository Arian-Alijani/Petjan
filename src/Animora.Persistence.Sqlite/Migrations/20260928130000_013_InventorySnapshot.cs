using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Animora.Persistence.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class _013_InventorySnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StockOnHandSnapshots",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    ProductId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    VariantId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    OnHandScaled = table.Column<long>(type: "INTEGER", nullable: false),
                    LastMovementUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    LastMovementId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockOnHandSnapshots", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StockOnHandSnapshots_TenantId_ProductId_VariantId",
                table: "StockOnHandSnapshots",
                columns: new[] { "TenantId", "ProductId", "VariantId" },
                unique: true);

            // Backfill: stores that already hold movements start with a proven cache (exact INTEGER sums; the
            // latest movement per key stamps LastMovement*). Ids are 26-char random hex — unique, never synced.
            migrationBuilder.Sql(
                """
                INSERT INTO "StockOnHandSnapshots" ("Id", "TenantId", "ProductId", "VariantId", "OnHandScaled", "LastMovementUtc", "LastMovementId")
                SELECT upper(hex(randomblob(13))), m."TenantId", m."ProductId", m."VariantId", SUM(m."QuantityScaled"),
                       MAX(m."OccurredUtc"),
                       (SELECT l."Id" FROM "StockMovements" l
                        WHERE l."TenantId" = m."TenantId" AND l."ProductId" = m."ProductId" AND l."VariantId" = m."VariantId" AND l."Deleted" = 0
                        ORDER BY l."OccurredUtc" DESC, l."Id" DESC LIMIT 1)
                FROM "StockMovements" m
                WHERE m."Deleted" = 0
                GROUP BY m."TenantId", m."ProductId", m."VariantId";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StockOnHandSnapshots");
        }
    }
}
