using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Animora.Persistence.PostgreSql.Migrations.Server
{
    /// <inheritdoc />
    public partial class _103_SigningKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SigningKeys",
                schema: "public",
                columns: table => new
                {
                    KeyId = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    Purpose = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Algorithm = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PublicKeyDer = table.Column<byte[]>(type: "bytea", nullable: false),
                    ProtectedPrivateKey = table.Column<byte[]>(type: "bytea", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "timestamp(6) with time zone", precision: 6, nullable: false),
                    NotAfterUtc = table.Column<DateTimeOffset>(type: "timestamp(6) with time zone", precision: 6, nullable: true),
                    RetiredUtc = table.Column<DateTimeOffset>(type: "timestamp(6) with time zone", precision: 6, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SigningKeys", x => x.KeyId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SigningKeys_Purpose_RetiredUtc",
                schema: "public",
                table: "SigningKeys",
                columns: new[] { "Purpose", "RetiredUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SigningKeys",
                schema: "public");
        }
    }
}
