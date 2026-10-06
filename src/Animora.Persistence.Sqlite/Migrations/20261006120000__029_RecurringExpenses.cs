using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Animora.Persistence.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class _029_RecurringExpenses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RecurringExpenses",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    CategoryCode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    DayOfMonth = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    SupplierId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: true),
                    Note = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    CreatedUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    AmountCurrency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    AmountMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    Version = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    Deleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecurringExpenses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RecurringExpensePayments",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    RecurringExpenseId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    PeriodCode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ExpenseId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    PaidUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    Version = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    Deleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecurringExpensePayments", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RecurringExpenses_TenantId_UpdatedAt",
                table: "RecurringExpenses",
                columns: new[] { "TenantId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RecurringExpensePayments_TenantId_RecurringExpenseId_PeriodCode",
                table: "RecurringExpensePayments",
                columns: new[] { "TenantId", "RecurringExpenseId", "PeriodCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecurringExpensePayments_TenantId_UpdatedAt",
                table: "RecurringExpensePayments",
                columns: new[] { "TenantId", "UpdatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RecurringExpensePayments");

            migrationBuilder.DropTable(
                name: "RecurringExpenses");
        }
    }
}
