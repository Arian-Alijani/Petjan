using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Animora.Persistence.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class _023_PatientsPerfIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Latest-weight correlated subquery in the Patients directory filters by PetId and orders
            // by MeasuredUtc DESC — this composite turns the per-row scan into an index seek + top-1.
            migrationBuilder.CreateIndex(
                name: "IX_WeightEntries_PetId_MeasuredUtc",
                table: "WeightEntries",
                columns: new[] { "PetId", "MeasuredUtc" });

            // Directory "Age" sort (OrderBy BirthDateUtc) — index instead of file-sort.
            migrationBuilder.CreateIndex(
                name: "IX_Pets_TenantId_BirthDateUtc",
                table: "Pets",
                columns: new[] { "TenantId", "BirthDateUtc" });

            // Directory "Recent"/"Age" sort (OrderBy[Descending] RegisteredUtc) — index instead of file-sort.
            migrationBuilder.CreateIndex(
                name: "IX_Owners_TenantId_RegisteredUtc",
                table: "Owners",
                columns: new[] { "TenantId", "RegisteredUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WeightEntries_PetId_MeasuredUtc",
                table: "WeightEntries");

            migrationBuilder.DropIndex(
                name: "IX_Pets_TenantId_BirthDateUtc",
                table: "Pets");

            migrationBuilder.DropIndex(
                name: "IX_Owners_TenantId_RegisteredUtc",
                table: "Owners");
        }
    }
}
