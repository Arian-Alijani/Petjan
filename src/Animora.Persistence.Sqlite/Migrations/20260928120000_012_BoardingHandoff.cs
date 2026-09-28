using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Animora.Persistence.Sqlite.Migrations;

public partial class _012_BoardingHandoff : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>(name: "FrozenTotalMinor", table: "BoardingStays",
            type: "INTEGER", nullable: true);
        migrationBuilder.CreateTable(name: "BoardingBillingHandoffs", columns: table => new
        {
            Id = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
            TenantId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
            StayId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
            OwnerId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
            PetId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
            DoctorId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
            CompletedUtc = table.Column<long>(type: "INTEGER", nullable: false),
            LinesJson = table.Column<string>(type: "TEXT", maxLength: 65536, nullable: false),
            Status = table.Column<int>(type: "INTEGER", nullable: false),
            InvoiceId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: true),
        }, constraints: table => table.PrimaryKey("PK_BoardingBillingHandoffs", x => x.Id));
        migrationBuilder.CreateIndex(name: "IX_BoardingBillingHandoffs_StayId", table: "BoardingBillingHandoffs",
            column: "StayId", unique: true);
        migrationBuilder.CreateIndex(name: "IX_BoardingBillingHandoffs_TenantId_Status_CompletedUtc",
            table: "BoardingBillingHandoffs", columns: new[] { "TenantId", "Status", "CompletedUtc" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "BoardingBillingHandoffs");
        migrationBuilder.DropColumn(name: "FrozenTotalMinor", table: "BoardingStays");
    }
}
