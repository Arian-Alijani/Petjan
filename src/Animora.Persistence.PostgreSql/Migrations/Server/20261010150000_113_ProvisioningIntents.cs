using Microsoft.EntityFrameworkCore.Migrations;
namespace Animora.Persistence.PostgreSql.Migrations.Server;

public partial class _113_ProvisioningIntents : Migration
{
    protected override void Up(MigrationBuilder mb) => mb.Sql("""
        CREATE TABLE public."WebsiteClients" ("Id" varchar(26) PRIMARY KEY, "KeyId" varchar(32) NOT NULL UNIQUE,
          "ProtectedSecret" bytea NOT NULL,"Status" smallint NOT NULL,"CreatedUtc" timestamptz(6) NOT NULL,
          "LastUsedUtc" timestamptz(6),"Label" varchar(100) NOT NULL);
        CREATE TABLE public."ProvisioningIntents" ("Id" varchar(26) PRIMARY KEY,"IdempotencyKey" varchar(128) NOT NULL UNIQUE,
          "ClientId" varchar(26) NOT NULL,"RequestFingerprint" character(64) NOT NULL,"PayloadProtected" bytea,
          "Status" smallint NOT NULL,"Attempts" integer NOT NULL,"NextAttemptUtc" timestamptz(6) NOT NULL,
          "ClaimToken" varchar(26),"ClaimedUntilUtc" timestamptz(6),"ResultJson" jsonb,"ProtectedTempPassword" bytea,
          "TempPasswordExpiresUtc" timestamptz(6),"ErrorCode" varchar(64),"ReceivedUtc" timestamptz(6) NOT NULL,"CompletedUtc" timestamptz(6),
          CHECK("Status" BETWEEN 0 AND 4),CHECK("Status"<>1 OR ("ClaimToken" IS NOT NULL AND "ClaimedUntilUtc" IS NOT NULL)));
        CREATE INDEX "IX_ProvisioningIntents_Status_NextAttemptUtc" ON public."ProvisioningIntents"("Status","NextAttemptUtc");
        """);
    protected override void Down(MigrationBuilder mb) => mb.Sql("""DROP TABLE public."ProvisioningIntents", public."WebsiteClients";""");
}
