using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Animora.Persistence.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class _026_VariantReorderScaled : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Variant reorder levels move from invariant-decimal TEXT to exact INTEGER x1000 (like OnHandScaled), so the
            // low-stock check is an integer compare with no float cast. Data is converted, never dropped.
            migrationBuilder.AddColumn<long>(
                name: "ReorderLevelScaled",
                table: "ProductVariants",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.Sql(
                "UPDATE \"ProductVariants\" SET \"ReorderLevelScaled\" = CAST(ROUND(CAST(\"ReorderLevelValue\" AS REAL) * 1000) AS INTEGER);");

            migrationBuilder.DropColumn(
                name: "ReorderLevelValue",
                table: "ProductVariants");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReorderLevelValue",
                table: "ProductVariants",
                type: "TEXT",
                nullable: false,
                defaultValue: "0");

            migrationBuilder.Sql(
                "UPDATE \"ProductVariants\" SET \"ReorderLevelValue\" = CAST(CAST(\"ReorderLevelScaled\" AS REAL) / 1000 AS TEXT);");

            migrationBuilder.DropColumn(
                name: "ReorderLevelScaled",
                table: "ProductVariants");
        }
    }
}
