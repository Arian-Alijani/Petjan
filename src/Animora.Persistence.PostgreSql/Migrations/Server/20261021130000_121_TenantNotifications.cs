using Animora.Persistence.PostgreSql.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Animora.Persistence.PostgreSql.Migrations.Server;

/// <summary>
/// Vendor-authored clinic messages: the server-side store behind the admin panel's message composer. The table is
/// vendor-global (written only through the operator context, like <c>OperatorAudit</c>); delivery to the desktop is
/// deferred to the sync track, so this migration only adds the authoritative store.
/// </summary>
[DbContext(typeof(AnimoraServerDbContext))]
[Migration("20261021130000_121_TenantNotifications")]
public sealed class _121_TenantNotifications : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE public."TenantNotifications" (
          "Id" varchar(26) PRIMARY KEY, "TenantId" varchar(26) NOT NULL,
          "AudienceKind" smallint NOT NULL CHECK ("AudienceKind" BETWEEN 0 AND 2), "AudienceValue" varchar(64),
          "Kind" smallint NOT NULL CHECK ("Kind" BETWEEN 0 AND 5), "Tag" varchar(40),
          "Title" varchar(200) NOT NULL, "Body" varchar(2000) NOT NULL, "CreatedByOperatorId" varchar(26) NOT NULL,
          "CreatedAtUtc" timestamptz(6) NOT NULL, "ScheduledForUtc" timestamptz(6), "ExpiresAtUtc" timestamptz(6));
        CREATE INDEX "IX_TenantNotifications_TenantId_CreatedAtUtc" ON public."TenantNotifications" ("TenantId","CreatedAtUtc");
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DROP TABLE public."TenantNotifications";
        """);
}
