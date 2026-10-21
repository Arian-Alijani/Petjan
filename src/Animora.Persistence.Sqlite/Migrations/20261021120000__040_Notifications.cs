using Animora.Persistence.Sqlite.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Animora.Persistence.Sqlite.Migrations;

/// <summary>
/// Local notifications &amp; messages: the message feed, per-user read receipts and the admin subscription-reward audit
/// trail. Purely additive (three new local-only tables); existing data is untouched.
/// </summary>
[DbContext(typeof(AnimoraLocalDbContext))]
[Migration("20261021120000__040_Notifications")]
public sealed class NotificationsTables : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Notifications",
            columns: table => new
            {
                Id = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                AudienceKind = table.Column<int>(type: "INTEGER", nullable: false),
                AudienceValue = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                Body = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                CreatedAtUtc = table.Column<long>(type: "INTEGER", nullable: false),
                CreatedByUserId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                DedupKey = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                ExpiresAtUtc = table.Column<long>(type: "INTEGER", nullable: true),
                Kind = table.Column<int>(type: "INTEGER", nullable: false),
                PublishedAtUtc = table.Column<long>(type: "INTEGER", nullable: true),
                ScheduledForUtc = table.Column<long>(type: "INTEGER", nullable: true),
                Tag = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                TenantId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_Notifications", x => x.Id));

        migrationBuilder.CreateTable(
            name: "NotificationReceipts",
            columns: table => new
            {
                TenantId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                UserId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                NotificationId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                ReadAtUtc = table.Column<long>(type: "INTEGER", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_NotificationReceipts", x => new { x.TenantId, x.UserId, x.NotificationId }));

        migrationBuilder.CreateTable(
            name: "RewardGrants",
            columns: table => new
            {
                Id = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                Days = table.Column<int>(type: "INTEGER", nullable: false),
                GrantedAtUtc = table.Column<long>(type: "INTEGER", nullable: false),
                GrantedByUserId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                Reason = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                TargetUserId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                TenantId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_RewardGrants", x => x.Id));

        migrationBuilder.CreateIndex(name: "IX_Notifications_TenantId_PublishedAtUtc", table: "Notifications",
            columns: new[] { "TenantId", "PublishedAtUtc" });
        migrationBuilder.CreateIndex(name: "IX_Notifications_TenantId_ScheduledForUtc", table: "Notifications",
            columns: new[] { "TenantId", "ScheduledForUtc" });
        migrationBuilder.CreateIndex(name: "IX_Notifications_TenantId_DedupKey", table: "Notifications",
            columns: new[] { "TenantId", "DedupKey" }, unique: true);
        migrationBuilder.CreateIndex(name: "IX_RewardGrants_TenantId_TargetUserId", table: "RewardGrants",
            columns: new[] { "TenantId", "TargetUserId" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "NotificationReceipts");
        migrationBuilder.DropTable(name: "Notifications");
        migrationBuilder.DropTable(name: "RewardGrants");
    }
}
