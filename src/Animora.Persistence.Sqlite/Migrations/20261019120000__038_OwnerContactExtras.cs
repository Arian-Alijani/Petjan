using Animora.Persistence.Sqlite.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Animora.Persistence.Sqlite.Migrations;

/// <summary>
/// Adds the owner contact extras the sheet must round-trip: the optional landline phone and the
/// free-form referral ("how the client found the clinic") note. Both columns are nullable so existing
/// rows default to "not recorded".
/// </summary>
[DbContext(typeof(AnimoraLocalDbContext))]
[Migration("20261019120000__038_OwnerContactExtras")]
public sealed class OwnerContactExtras : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "PhoneLandline", table: "Owners",
            type: "TEXT", maxLength: 40, nullable: true);
        migrationBuilder.AddColumn<string>(name: "Referral", table: "Owners",
            type: "TEXT", maxLength: 200, nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "PhoneLandline", table: "Owners");
        migrationBuilder.DropColumn(name: "Referral", table: "Owners");
    }
}
