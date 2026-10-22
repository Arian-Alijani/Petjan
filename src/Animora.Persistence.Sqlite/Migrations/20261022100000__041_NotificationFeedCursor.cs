using Animora.Persistence.Sqlite.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Animora.Persistence.Sqlite.Migrations;

/// <summary>
/// Vendor-message delivery: the per-tenant pull cursor of <c>GET /notifications</c>. Purely additive (one new
/// local-only table); existing data and the Notifications table are untouched.
/// </summary>
[DbContext(typeof(AnimoraLocalDbContext))]
[Migration("20261022100000__041_NotificationFeedCursor")]
public sealed class NotificationFeedCursorTable : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.CreateTable(
            name: "NotificationFeedCursors",
            columns: table => new
            {
                TenantId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                CursorTicks = table.Column<long>(type: "INTEGER", nullable: false),
                ReportedTicks = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_NotificationFeedCursors", x => x.TenantId));

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "NotificationFeedCursors");
}
