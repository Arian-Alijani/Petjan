using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Animora.Persistence.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class _021_DashboardGeneration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DashboardGenerations",
                columns: table => new
                {
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    Value = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DashboardGenerations", x => x.TenantId);
                });

            // Every table the dashboard reads advances its tenant's generation in the same transaction as the write
            // (insert/update/delete, including soft-delete updates, projection-worker facts and projection rebuilds).
            foreach (var (table, tenantSelect) in Sources())
            {
                foreach (var (suffix, operation, row) in Operations)
                {
                    var tenant = tenantSelect.Replace("{row}", row);
                    migrationBuilder.Sql(
                        $"CREATE TRIGGER \"TR_{table}_DashboardGeneration_{suffix}\" AFTER {operation} ON \"{table}\" BEGIN " +
                        $"INSERT INTO \"DashboardGenerations\" (\"TenantId\", \"Value\") {tenant} " +
                        "ON CONFLICT (\"TenantId\") DO UPDATE SET \"Value\" = \"Value\" + 1; END;");
                }
            }
        }

        private static readonly (string Suffix, string Operation, string Row)[] Operations =
        [
            ("Insert", "INSERT", "NEW"), ("Update", "UPDATE", "NEW"), ("Delete", "DELETE", "OLD"),
        ];

        // Rows of these tables carry their own TenantId.
        private static readonly string[] TenantTables =
        [
            "DailyFinanceFacts", "ActivityEvents", "Payments", "Invoices", "Appointments", "ReceptionQueueEntries",
            "Visits", "VaccinationRecords", "MedicalHistoryEntries", "VisitBillingHandoffs", "BoardingBillingHandoffs",
            "Owners", "Pets", "Users", "Products", "StockOnHandSnapshots",
        ];

        // ProductVariants has no tenant column and resolves it through its product (a variant removed by a product
        // cascade finds no product and records nothing; the product delete itself already advanced the generation).
        private const string VariantTable = "ProductVariants";

        private static IEnumerable<(string Table, string Select)> Sources() =>
            TenantTables.Select(t => (t, "SELECT {row}.\"TenantId\", 1 WHERE 1"))
                .Append((VariantTable, "SELECT \"TenantId\", 1 FROM \"Products\" WHERE \"Id\" = {row}.\"OwnerId\""));

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var (table, _) in Sources())
            {
                foreach (var (suffix, _, _) in Operations)
                    migrationBuilder.Sql($"DROP TRIGGER IF EXISTS \"TR_{table}_DashboardGeneration_{suffix}\";");
            }

            migrationBuilder.DropTable(
                name: "DashboardGenerations");
        }
    }
}
