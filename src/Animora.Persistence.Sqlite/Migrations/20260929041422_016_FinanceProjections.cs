using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Animora.Persistence.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class _016_FinanceProjections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DailyFinanceFacts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    LocalDay = table.Column<int>(type: "INTEGER", nullable: false),
                    RevenueMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    CogsMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    ExpenseMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    DiscountMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    InsuranceMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    RefundMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    InvoiceCount = table.Column<int>(type: "INTEGER", nullable: false),
                    PaidInvoiceCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyFinanceFacts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OwnerFinancialSnapshots",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    TotalSpendMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    OpenBalanceMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    LastInvoiceUtc = table.Column<long>(type: "INTEGER", nullable: true),
                    InvoiceCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OwnerFinancialSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProductSalesFacts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    LocalDay = table.Column<int>(type: "INTEGER", nullable: false),
                    ProductId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    VariantId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    QuantitySold = table.Column<string>(type: "TEXT", nullable: false),
                    RevenueMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    CogsMinor = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductSalesFacts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DailyFinanceFacts_TenantId_LocalDay",
                table: "DailyFinanceFacts",
                columns: new[] { "TenantId", "LocalDay" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OwnerFinancialSnapshots_TenantId_OwnerId",
                table: "OwnerFinancialSnapshots",
                columns: new[] { "TenantId", "OwnerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductSalesFacts_TenantId_LocalDay_ProductId_VariantId",
                table: "ProductSalesFacts",
                columns: new[] { "TenantId", "LocalDay", "ProductId", "VariantId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DailyFinanceFacts");

            migrationBuilder.DropTable(
                name: "OwnerFinancialSnapshots");

            migrationBuilder.DropTable(
                name: "ProductSalesFacts");
        }
    }
}
