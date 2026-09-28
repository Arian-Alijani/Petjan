using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Animora.Persistence.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class _010_C03Schema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SmsTemplateRows",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Channel = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    Body = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SmsTemplateRows", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VisitBillingHandoffs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    VisitId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    PetId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    DoctorId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    CompletedUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    LinesJson = table.Column<string>(type: "TEXT", maxLength: 65536, nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    InvoiceId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VisitBillingHandoffs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SmsTemplateRows_TenantId_Name",
                table: "SmsTemplateRows",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VisitBillingHandoffs_TenantId_Status_CompletedUtc",
                table: "VisitBillingHandoffs",
                columns: new[] { "TenantId", "Status", "CompletedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_VisitBillingHandoffs_VisitId",
                table: "VisitBillingHandoffs",
                column: "VisitId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SmsTemplateRows");

            migrationBuilder.DropTable(
                name: "VisitBillingHandoffs");
        }
    }
}
