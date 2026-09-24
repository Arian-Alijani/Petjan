using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Animora.Persistence.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class _004_CommandPipeline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LocalSequenceCounters",
                columns: table => new
                {
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Value = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LocalSequenceCounters", x => x.Name);
                });

            migrationBuilder.CreateTable(
                name: "OutboxMessages",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    MessageType = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    PayloadJson = table.Column<string>(type: "TEXT", maxLength: 65536, nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    ClaimedUntilUtc = table.Column<long>(type: "INTEGER", nullable: true),
                    ClaimToken = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    Attempts = table.Column<int>(type: "INTEGER", nullable: false),
                    NextAttemptUtc = table.Column<long>(type: "INTEGER", nullable: true),
                    LastErrorCode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    ProcessedAtUtc = table.Column<long>(type: "INTEGER", nullable: true),
                    CorrelationId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PendingCommands",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    OperationId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    DeviceId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    CommandType = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    AggregateType = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    AggregateId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: true),
                    PayloadJson = table.Column<string>(type: "TEXT", maxLength: 65536, nullable: false),
                    CommandFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    LocalSequence = table.Column<long>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    CausationId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: true),
                    DependsOnOperationId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: true),
                    ExpectedAggregateVersion = table.Column<long>(type: "INTEGER", nullable: true),
                    CreatedAtUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    LastAttemptUtc = table.Column<long>(type: "INTEGER", nullable: true),
                    Attempts = table.Column<int>(type: "INTEGER", nullable: false),
                    NextAttemptUtc = table.Column<long>(type: "INTEGER", nullable: true),
                    LastErrorCode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    LastErrorDetail = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    FinancialEffect = table.Column<int>(type: "INTEGER", nullable: false),
                    ResponsibleRole = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    SettledAtUtc = table.Column<long>(type: "INTEGER", nullable: true),
                    ServerEntityId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: true),
                    ServerVersion = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    ClaimedUntilUtc = table.Column<long>(type: "INTEGER", nullable: true),
                    ClaimToken = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PendingCommands", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_MessageType",
                table: "OutboxMessages",
                column: "MessageType");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_Status_NextAttemptUtc",
                table: "OutboxMessages",
                columns: new[] { "Status", "NextAttemptUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PendingCommands_AggregateType_AggregateId",
                table: "PendingCommands",
                columns: new[] { "AggregateType", "AggregateId" });

            migrationBuilder.CreateIndex(
                name: "IX_PendingCommands_DependsOnOperationId",
                table: "PendingCommands",
                column: "DependsOnOperationId");

            migrationBuilder.CreateIndex(
                name: "IX_PendingCommands_LocalSequence",
                table: "PendingCommands",
                column: "LocalSequence",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PendingCommands_OperationId",
                table: "PendingCommands",
                column: "OperationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PendingCommands_Status_NextAttemptUtc",
                table: "PendingCommands",
                columns: new[] { "Status", "NextAttemptUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LocalSequenceCounters");

            migrationBuilder.DropTable(
                name: "OutboxMessages");

            migrationBuilder.DropTable(
                name: "PendingCommands");
        }
    }
}
