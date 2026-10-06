using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Animora.Persistence.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class _030_BoardingPrepaidRoom : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A prepaid stay raises two handoffs over its life (prepaid room at admission, then the
            // final settlement at checkout), so the StayId index can no longer be unique.
            migrationBuilder.DropIndex(
                name: "IX_BoardingBillingHandoffs_StayId",
                table: "BoardingBillingHandoffs");

            migrationBuilder.AddColumn<int>(
                name: "Kind",
                table: "BoardingBillingHandoffs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_BoardingBillingHandoffs_StayId",
                table: "BoardingBillingHandoffs",
                column: "StayId");

            migrationBuilder.AddColumn<string>(
                name: "PrepaidInvoiceId",
                table: "BoardingStays",
                type: "TEXT",
                maxLength: 26,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "PrepaidRoomChargeMinor",
                table: "BoardingStays",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PrepaidRoomChargeMinor",
                table: "BoardingStays");

            migrationBuilder.DropColumn(
                name: "PrepaidInvoiceId",
                table: "BoardingStays");

            migrationBuilder.DropIndex(
                name: "IX_BoardingBillingHandoffs_StayId",
                table: "BoardingBillingHandoffs");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "BoardingBillingHandoffs");

            migrationBuilder.CreateIndex(
                name: "IX_BoardingBillingHandoffs_StayId",
                table: "BoardingBillingHandoffs",
                column: "StayId",
                unique: true);
        }
    }
}
