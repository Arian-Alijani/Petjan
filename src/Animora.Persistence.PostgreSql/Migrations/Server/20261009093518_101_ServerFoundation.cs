using System;
using Animora.Persistence.PostgreSql.Concurrency;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Animora.Persistence.PostgreSql.Migrations.Server
{
    /// <inheritdoc />
    public partial class _101_ServerFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Hand-edited (S02): the database owns "Version"; every INSERT/UPDATE (incl. raw SQL) regenerates it.
            migrationBuilder.Sql(@"CREATE OR REPLACE FUNCTION animora_set_version() RETURNS trigger AS $$ BEGIN NEW.""Version"" := gen_random_uuid(); RETURN NEW; END; $$ LANGUAGE plpgsql;");

            migrationBuilder.EnsureSchema(
                name: "public");

            migrationBuilder.CreateTable(
                name: "Tenants",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    LegalName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Timezone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DefaultCurrency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp(6) with time zone", precision: 6, nullable: false),
                    Version = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    Deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tenants", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_TenantId_Status",
                schema: "public",
                table: "Tenants",
                columns: new[] { "TenantId", "Status" });

            VersionTrigger.Apply(migrationBuilder, "Tenants");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            VersionTrigger.Drop(migrationBuilder, "Tenants");

            migrationBuilder.Sql("DROP FUNCTION IF EXISTS animora_set_version();");

            migrationBuilder.DropTable(
                name: "Tenants",
                schema: "public");
        }
    }
}
