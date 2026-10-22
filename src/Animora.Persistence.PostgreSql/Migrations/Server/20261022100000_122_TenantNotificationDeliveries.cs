using Animora.Persistence.PostgreSql.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Animora.Persistence.PostgreSql.Migrations.Server;

/// <summary>
/// Real per-device delivery receipts for vendor-authored clinic messages: one row per (message, device) written
/// when a desktop client confirms it stored the message locally. Backs the admin delivery report ("how many
/// clients received it") and the resend-to-undelivered action. The unique key makes repeated reports idempotent.
/// </summary>
[DbContext(typeof(AnimoraServerDbContext))]
[Migration("20261022100000_122_TenantNotificationDeliveries")]
public sealed class _122_TenantNotificationDeliveries : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE public."TenantNotificationDeliveries" (
          "Id" varchar(26) PRIMARY KEY, "TenantId" varchar(26) NOT NULL, "NotificationId" varchar(26) NOT NULL,
          "DeviceId" varchar(26) NOT NULL, "UserId" varchar(26) NOT NULL, "ReceivedUtc" timestamptz(6) NOT NULL);
        CREATE UNIQUE INDEX "UX_TenantNotificationDeliveries_NotificationId_DeviceId" ON public."TenantNotificationDeliveries" ("NotificationId","DeviceId");
        CREATE INDEX "IX_TenantNotificationDeliveries_TenantId_NotificationId" ON public."TenantNotificationDeliveries" ("TenantId","NotificationId");
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DROP TABLE public."TenantNotificationDeliveries";
        """);
}
