using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Animora.Persistence.Sqlite.Migrations;

public partial class _009_PatientsSearchKeys : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "SearchKey", table: "Owners", type: "TEXT",
            maxLength: 400, nullable: false, defaultValue: "");
        migrationBuilder.AddColumn<string>(name: "SearchKey", table: "Pets", type: "TEXT",
            maxLength: 400, nullable: false, defaultValue: "");

        migrationBuilder.CreateIndex(name: "IX_Owners_TenantId_SearchKey", table: "Owners",
            columns: new[] { "TenantId", "SearchKey" });
        migrationBuilder.CreateIndex(name: "IX_Pets_TenantId_SearchKey", table: "Pets",
            columns: new[] { "TenantId", "SearchKey" });

        // Backfill existing rows with a best-effort lowercased key so pre-existing data is searchable
        // before its next write re-stamps the full normalized key. SQLite has lower() but no Persian folding;
        // the next SaveChanges on each row will overwrite with the fully-normalized value.
        migrationBuilder.Sql(
            "UPDATE Owners SET SearchKey = lower(trim(FullName || ' ' || PhonePrimary || ' ' || " +
            "COALESCE(PhoneSecondary,'') || ' ' || COALESCE(NationalId,'')));");
        migrationBuilder.Sql(
            "UPDATE Pets SET SearchKey = lower(trim(Name || ' ' || COALESCE(Breed,'') || ' ' || " +
            "COALESCE(MicrochipCode,'')));");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_Owners_TenantId_SearchKey", table: "Owners");
        migrationBuilder.DropIndex(name: "IX_Pets_TenantId_SearchKey", table: "Pets");
        migrationBuilder.DropColumn(name: "SearchKey", table: "Owners");
        migrationBuilder.DropColumn(name: "SearchKey", table: "Pets");
    }
}
