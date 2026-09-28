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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StockOnHandSnapshots");
        }
    }
}
