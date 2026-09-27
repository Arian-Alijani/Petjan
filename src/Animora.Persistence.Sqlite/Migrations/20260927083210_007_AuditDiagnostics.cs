using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Animora.Persistence.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class _007_AuditDiagnostics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditEntries",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    DeviceId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    OccurredAtUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    Category = table.Column<int>(type: "INTEGER", nullable: false),
                    Action = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    EntityType = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    EntityId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: true),
                    OperationId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    CorrelationId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    Outcome = table.Column<int>(type: "INTEGER", nullable: false),
                    BeforeRef = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    AfterRef = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    AmountMinor = table.Column<long>(type: "INTEGER", nullable: true),
                    AmountCurrency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: true),
                    Detail = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEntries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_Category_OccurredAtUtc",
                table: "AuditEntries",
                columns: new[] { "Category", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_EntityType_EntityId",
                table: "AuditEntries",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_OccurredAtUtc",
                table: "AuditEntries",
                column: "OccurredAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_OperationId",
                table: "AuditEntries",
                column: "OperationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditEntries");
        }
    }
}
