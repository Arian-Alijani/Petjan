using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Animora.Persistence.Sqlite.Migrations;

/// <summary>Indexes the pre-existing cash-session link on invoice payments.</summary>
public partial class _015_CashSessionLink : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.CreateIndex(
        name: "IX_Payments_TenantId_CashSessionId",
        table: "Payments",
        columns: new[] { "TenantId", "CashSessionId" });

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropIndex(
        name: "IX_Payments_TenantId_CashSessionId",
        table: "Payments");
}
