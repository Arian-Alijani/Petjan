using Microsoft.EntityFrameworkCore.Migrations;
#nullable disable
namespace Animora.Persistence.PostgreSql.Migrations.Server;

public partial class _114_Licensing : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE public."LicenseGrants" (
                "TenantId" varchar(26) PRIMARY KEY, "SubscriptionId" varchar(26) NOT NULL,
                "PlanCode" varchar(32) NOT NULL, "SeatLimit" integer NOT NULL, "MaxDevices" integer NOT NULL,
                "ValidUntilUtc" timestamptz(6) NOT NULL, "Status" smallint NOT NULL,
                "Sequence" bigint NOT NULL, "SourceEventSequence" integer NOT NULL, "UpdatedUtc" timestamptz(6) NOT NULL);
            CREATE TABLE public."Licenses" (
                "TenantId" varchar(26) NOT NULL, "Id" varchar(26) NOT NULL, "DeviceId" varchar(26) NOT NULL,
                "InstallationIdHash" char(64) NOT NULL, "Sequence" bigint NOT NULL,
                "IssuedUtc" timestamptz(6) NOT NULL, "ExpiresUtc" timestamptz(6) NOT NULL,
                "OfflineGraceUntilUtc" timestamptz(6) NOT NULL, "KeyId" varchar(26) NOT NULL,
                "PayloadSha256" char(64) NOT NULL, "Status" smallint NOT NULL,
                "RevokedUtc" timestamptz(6), "RevokedByActor" varchar(26), "RevokeReason" varchar(200),
                CONSTRAINT "PK_Licenses" PRIMARY KEY ("TenantId","Id"));
            CREATE UNIQUE INDEX "UX_Licenses_Tenant_Device_Active" ON public."Licenses" ("TenantId","DeviceId") WHERE "Status"=0;
            CREATE INDEX "IX_Licenses_TenantId_Status" ON public."Licenses" ("TenantId","Status");
            INSERT INTO public."LicenseGrants" ("TenantId","SubscriptionId","PlanCode","SeatLimit","MaxDevices",
                "ValidUntilUtc","Status","Sequence","SourceEventSequence","UpdatedUtc")
            SELECT s."TenantId",s."Id",p."Code",s."SeatLimit",s."SeatLimit"*2,
                COALESCE(s."GraceEndsUtc",s."CurrentPeriodEndUtc"),
                CASE WHEN s."Status" IN (0,1,2) THEN 0 WHEN s."Status"=4 THEN 2 ELSE 1 END,
                1,s."NextEventSequence"-1,s."UpdatedUtc"
            FROM public."Subscriptions" s JOIN public."Plans" p ON p."Id"=s."PlanId"
            ON CONFLICT ("TenantId") DO NOTHING;
            """);
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("Licenses", "public");
        migrationBuilder.DropTable("LicenseGrants", "public");
    }
}
