using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Animora.Persistence.PostgreSql.Migrations.Server
{
    /// <inheritdoc />
    public partial class _104_CommandIntake : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CommandIdMap",
                schema: "public",
                columns: table => new
                {
                    TenantId = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    OperationId = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    Slot = table.Column<short>(type: "smallint", nullable: false),
                    EntityType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ServerEntityId = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommandIdMap", x => new { x.TenantId, x.OperationId, x.Slot });
                });

            migrationBuilder.CreateTable(
                name: "CommandIntake",
                schema: "public",
                columns: table => new
                {
                    TenantId = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    OperationId = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    CommandType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Fingerprint = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    Outcome = table.Column<short>(type: "smallint", nullable: true),
                    ErrorCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Detail = table.Column<string>(type: "text", maxLength: 512, nullable: true),
                    ServerEntityId = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: true),
                    ServerVersion = table.Column<Guid>(type: "uuid", nullable: true),
                    FinancialEffect = table.Column<short>(type: "smallint", nullable: false),
                    ResponsibleRole = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    ReceivedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", precision: 6, nullable: false),
                    SettledUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", precision: 6, nullable: true),
                    AttemptId = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: true),
                    LeaseOwner = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    LeaseUntilUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", precision: 6, nullable: true),
                    AttemptCount = table.Column<short>(type: "smallint", nullable: false),
                    PayloadJson = table.Column<string>(type: "jsonb", nullable: true),
                    DependsOnOperationId = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: true),
                    CorrelationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommandIntake", x => new { x.TenantId, x.OperationId });
                });

            migrationBuilder.CreateIndex(
                name: "IX_CommandIntake_TenantId_DependsOnOperationId",
                schema: "public",
                table: "CommandIntake",
                columns: new[] { "TenantId", "DependsOnOperationId" });

            migrationBuilder.CreateIndex(
                name: "IX_CommandIntake_TenantId_Status_LeaseUntilUtc",
                schema: "public",
                table: "CommandIntake",
                columns: new[] { "TenantId", "Status", "LeaseUntilUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CommandIdMap",
                schema: "public");

            migrationBuilder.DropTable(
                name: "CommandIntake",
                schema: "public");
        }
    }
}
