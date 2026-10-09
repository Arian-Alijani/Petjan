using System;
using Animora.Persistence.PostgreSql.Concurrency;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Animora.Persistence.PostgreSql.Migrations.Server
{
    /// <inheritdoc />
    public partial class _102_Identity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Code",
                schema: "public",
                table: "Tenants",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Devices",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    UserId = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    DeviceName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    InstallationIdHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Platform = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    RegisteredUtc = table.Column<DateTimeOffset>(type: "timestamp(6) with time zone", precision: 6, nullable: false),
                    LastContactUtc = table.Column<DateTimeOffset>(type: "timestamp(6) with time zone", precision: 6, nullable: true),
                    RevokedUtc = table.Column<DateTimeOffset>(type: "timestamp(6) with time zone", precision: 6, nullable: true),
                    RevocationReason = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    TenantId = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp(6) with time zone", precision: 6, nullable: false),
                    Version = table.Column<Guid>(type: "uuid", rowVersion: true, nullable: false, defaultValueSql: "gen_random_uuid()"),
                    Deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Devices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RefreshTokens",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    TenantId = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    UserId = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    DeviceId = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FamilyId = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    IssuedUtc = table.Column<DateTimeOffset>(type: "timestamp(6) with time zone", precision: 6, nullable: false),
                    ExpiresUtc = table.Column<DateTimeOffset>(type: "timestamp(6) with time zone", precision: 6, nullable: false),
                    RotatedToId = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: true),
                    RevokedUtc = table.Column<DateTimeOffset>(type: "timestamp(6) with time zone", precision: 6, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefreshTokens", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RoleDefinitions",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    DisplayKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IsSystem = table.Column<bool>(type: "boolean", nullable: false),
                    PermissionCodes = table.Column<string[]>(type: "text[]", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp(6) with time zone", precision: 6, nullable: false),
                    Version = table.Column<Guid>(type: "uuid", rowVersion: true, nullable: false, defaultValueSql: "gen_random_uuid()"),
                    Deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoleDefinitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SecurityAudit",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    TenantId = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    OccurredUtc = table.Column<DateTimeOffset>(type: "timestamp(6) with time zone", precision: 6, nullable: false),
                    ActorUserId = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: true),
                    DeviceId = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: true),
                    Category = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Outcome = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DetailJson = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecurityAudit", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StaffMemberships",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    UserId = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    JoinedUtc = table.Column<DateTimeOffset>(type: "timestamp(6) with time zone", precision: 6, nullable: false),
                    EndedUtc = table.Column<DateTimeOffset>(type: "timestamp(6) with time zone", precision: 6, nullable: true),
                    WorkStartMinute = table.Column<int>(type: "integer", nullable: false),
                    WorkEndMinute = table.Column<int>(type: "integer", nullable: false),
                    WorkDaysMask = table.Column<int>(type: "integer", nullable: false),
                    DeniedPermissionCodes = table.Column<string[]>(type: "text[]", nullable: false),
                    ExtraPermissionCodes = table.Column<string[]>(type: "text[]", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp(6) with time zone", precision: 6, nullable: false),
                    Version = table.Column<Guid>(type: "uuid", rowVersion: true, nullable: false, defaultValueSql: "gen_random_uuid()"),
                    Deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffMemberships", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    UserName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PasswordHash = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    SecurityStamp = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    LastLoginUtc = table.Column<DateTimeOffset>(type: "timestamp(6) with time zone", precision: 6, nullable: true),
                    FailedAttemptCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    LockoutEndUtc = table.Column<DateTimeOffset>(type: "timestamp(6) with time zone", precision: 6, nullable: true),
                    MustChangePassword = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    FirstFailureUtc = table.Column<DateTimeOffset>(type: "timestamp(6) with time zone", precision: 6, nullable: true),
                    TenantId = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp(6) with time zone", precision: 6, nullable: false),
                    Version = table.Column<Guid>(type: "uuid", rowVersion: true, nullable: false, defaultValueSql: "gen_random_uuid()"),
                    Deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "UX_Tenants_Code",
                schema: "public",
                table: "Tenants",
                column: "Code",
                unique: true,
                filter: "\"Code\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Devices_TenantId_InstallationIdHash_UserId",
                schema: "public",
                table: "Devices",
                columns: new[] { "TenantId", "InstallationIdHash", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_TenantId_FamilyId",
                schema: "public",
                table: "RefreshTokens",
                columns: new[] { "TenantId", "FamilyId" });

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_TenantId_UserId",
                schema: "public",
                table: "RefreshTokens",
                columns: new[] { "TenantId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_TokenHash",
                schema: "public",
                table: "RefreshTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RoleDefinitions_TenantId_Role",
                schema: "public",
                table: "RoleDefinitions",
                columns: new[] { "TenantId", "Role" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SecurityAudit_TenantId_OccurredUtc",
                schema: "public",
                table: "SecurityAudit",
                columns: new[] { "TenantId", "OccurredUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_StaffMemberships_TenantId_UserId",
                schema: "public",
                table: "StaffMemberships",
                columns: new[] { "TenantId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_Users_TenantId_UserName",
                schema: "public",
                table: "Users",
                columns: new[] { "TenantId", "UserName" },
                unique: true);

            // Hand-edited (S04): the database owns "Version" on the four SyncableEntity tables.
            VersionTrigger.Apply(migrationBuilder, "Users");
            VersionTrigger.Apply(migrationBuilder, "StaffMemberships");
            VersionTrigger.Apply(migrationBuilder, "RoleDefinitions");
            VersionTrigger.Apply(migrationBuilder, "Devices");

            // Hand-edited (S04): SecurityAudit is append-only forever — any UPDATE/DELETE is rejected.
            migrationBuilder.Sql(
                "CREATE OR REPLACE FUNCTION animora_securityaudit_immutable() RETURNS trigger AS $$ " +
                "BEGIN RAISE EXCEPTION 'SecurityAudit is append-only' USING ERRCODE = 'restrict_violation'; END; " +
                "$$ LANGUAGE plpgsql;");
            migrationBuilder.Sql(
                "CREATE TRIGGER trg_SecurityAudit_immutable BEFORE UPDATE OR DELETE ON \"SecurityAudit\" " +
                "FOR EACH ROW EXECUTE FUNCTION animora_securityaudit_immutable();");
            // TRUNCATE bypasses row triggers, so guard it with a statement-level trigger too.
            migrationBuilder.Sql(
                "CREATE TRIGGER trg_SecurityAudit_no_truncate BEFORE TRUNCATE ON \"SecurityAudit\" " +
                "FOR EACH STATEMENT EXECUTE FUNCTION animora_securityaudit_immutable();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_SecurityAudit_no_truncate ON \"SecurityAudit\";");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_SecurityAudit_immutable ON \"SecurityAudit\";");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS animora_securityaudit_immutable();");

            VersionTrigger.Drop(migrationBuilder, "Devices");
            VersionTrigger.Drop(migrationBuilder, "RoleDefinitions");
            VersionTrigger.Drop(migrationBuilder, "StaffMemberships");
            VersionTrigger.Drop(migrationBuilder, "Users");

            migrationBuilder.DropTable(
                name: "Devices",
                schema: "public");

            migrationBuilder.DropTable(
                name: "RefreshTokens",
                schema: "public");

            migrationBuilder.DropTable(
                name: "RoleDefinitions",
                schema: "public");

            migrationBuilder.DropTable(
                name: "SecurityAudit",
                schema: "public");

            migrationBuilder.DropTable(
                name: "StaffMemberships",
                schema: "public");

            migrationBuilder.DropTable(
                name: "Users",
                schema: "public");

            migrationBuilder.DropIndex(
                name: "UX_Tenants_Code",
                schema: "public",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "Code",
                schema: "public",
                table: "Tenants");
        }
    }
}
