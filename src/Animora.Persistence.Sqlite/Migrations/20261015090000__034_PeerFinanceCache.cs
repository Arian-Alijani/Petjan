using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Animora.Persistence.Sqlite.Migrations
{
    /// <summary>
    /// D04b: the read-only, server-authored peer finance cache (<c>PeerInvoices</c>, <c>PeerPayments</c>) and its poll cursor
    /// (<c>PeerFinanceCursors</c>). All three are local-only display projections; nothing here feeds the command pipeline.
    /// </summary>
    public partial class _034_PeerFinanceCache : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PeerFinanceCursors",
                columns: table => new
                {
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    CursorTicks = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PeerFinanceCursors", x => x.TenantId);
                });

            migrationBuilder.CreateTable(
                name: "PeerInvoices",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    DueTotalMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    EffectiveTicks = table.Column<long>(type: "INTEGER", nullable: false),
                    GrandTotalMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    IssuedUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Number = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: true),
                    PaidTotalMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    PartyName = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    PetName = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    PostedUtc = table.Column<long>(type: "INTEGER", nullable: true),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PeerInvoices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PeerPayments",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    AmountMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    InvoiceId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    Method = table.Column<int>(type: "INTEGER", nullable: false),
                    ReceivedUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PeerPayments", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PeerInvoices_TenantId_IssuedUtc",
                table: "PeerInvoices",
                columns: new[] { "TenantId", "IssuedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PeerPayments_InvoiceId",
                table: "PeerPayments",
                column: "InvoiceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PeerFinanceCursors");

            migrationBuilder.DropTable(
                name: "PeerInvoices");

            migrationBuilder.DropTable(
                name: "PeerPayments");
        }
    }
}
