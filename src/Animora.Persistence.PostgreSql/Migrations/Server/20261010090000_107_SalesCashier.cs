using System;
using Animora.Persistence.PostgreSql.Concurrency;
using Microsoft.EntityFrameworkCore.Migrations;
#nullable disable
namespace Animora.Persistence.PostgreSql.Migrations.Server;

// Authored without invoking EF tooling, which would build the project.
public partial class _107_SalesCashier : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(name: "Invoices", schema: "public", columns: t => new
        {
            Id = t.Column<string>(type: "character varying(26)", nullable: false, maxLength: 26),
            TenantId = t.Column<string>(type: "character varying(26)", nullable: false, maxLength: 26),
            UpdatedAt = t.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, precision: 6),
            Version = t.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
            Deleted = t.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
            InvoiceNumber = t.Column<string>(type: "text", nullable: false),
            Kind = t.Column<int>(type: "integer", nullable: false),
            State = t.Column<int>(type: "integer", nullable: false),
            OwnerId = t.Column<string>(type: "character varying(26)", nullable: true, maxLength: 26),
            PetId = t.Column<string>(type: "text", nullable: true),
            VisitId = t.Column<string>(type: "text", nullable: true),
            BoardingStayId = t.Column<string>(type: "text", nullable: true),
            GuestName = t.Column<string>(type: "text", nullable: true),
            IssuedUtc = t.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            PostedUtc = t.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
            SettledUtc = t.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
            SubtotalMinor = t.Column<long>(type: "bigint", nullable: false),
            SubtotalCurrency = t.Column<string>(type: "character varying(3)", nullable: false, maxLength: 3),
            DiscountKind = t.Column<int>(type: "integer", nullable: false),
            DiscountValue = t.Column<decimal>(type: "numeric", nullable: false),
            DiscountTotalMinor = t.Column<long>(type: "bigint", nullable: false),
            DiscountTotalCurrency = t.Column<string>(type: "character varying(3)", nullable: false, maxLength: 3),
            TaxTotalMinor = t.Column<long>(type: "bigint", nullable: false),
            TaxTotalCurrency = t.Column<string>(type: "character varying(3)", nullable: false, maxLength: 3),
            InsurerId = t.Column<string>(type: "text", nullable: true),
            InsurancePercent = t.Column<decimal>(type: "numeric", nullable: false),
            InsuranceCoverageMinor = t.Column<long>(type: "bigint", nullable: false),
            InsuranceCoverageCurrency = t.Column<string>(type: "character varying(3)", nullable: false, maxLength: 3),
            GrandTotalMinor = t.Column<long>(type: "bigint", nullable: false),
            GrandTotalCurrency = t.Column<string>(type: "character varying(3)", nullable: false, maxLength: 3),
            PaidTotalMinor = t.Column<long>(type: "bigint", nullable: false),
            PaidTotalCurrency = t.Column<string>(type: "character varying(3)", nullable: false, maxLength: 3),
            DueTotalMinor = t.Column<long>(type: "bigint", nullable: false),
            DueTotalCurrency = t.Column<string>(type: "character varying(3)", nullable: false, maxLength: 3),
            Settlement = t.Column<int>(type: "integer", nullable: false),
            OperationId = t.Column<string>(type: "text", nullable: true),
            CreatedByUserId = t.Column<string>(type: "text", nullable: false),
            Notes = t.Column<string>(type: "text", nullable: true),
            SettlementErrorCode = t.Column<string>(type: "text", nullable: true),
            AppliedPaymentOperationIds = t.Column<string>(type: "jsonb", nullable: false)
        }, constraints: t => t.PrimaryKey("PK_Invoices", x => x.Id));
        migrationBuilder.CreateIndex(name: "IX_Invoices_TenantId_State_IssuedUtc", schema: "public", table: "Invoices", columns: new[] { "TenantId", "State", "IssuedUtc" }, unique: false);
        migrationBuilder.CreateIndex(name: "IX_Invoices_TenantId_Number", schema: "public", table: "Invoices", columns: new[] { "TenantId", "InvoiceNumber" }, unique: true);
        migrationBuilder.CreateIndex(name: "IX_Invoices_TenantId_OwnerId", schema: "public", table: "Invoices", columns: new[] { "TenantId", "OwnerId" }, unique: false);
        migrationBuilder.CreateIndex(name: "IX_Invoices_TenantId_VisitId", schema: "public", table: "Invoices", columns: new[] { "TenantId", "VisitId" }, unique: false);
        VersionTrigger.Apply(migrationBuilder, "Invoices");
        migrationBuilder.CreateTable(name: "Payments", schema: "public", columns: t => new
        {
            Id = t.Column<string>(type: "character varying(26)", nullable: false, maxLength: 26),
            TenantId = t.Column<string>(type: "character varying(26)", nullable: false, maxLength: 26),
            UpdatedAt = t.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, precision: 6),
            Version = t.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
            Deleted = t.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
            InvoiceId = t.Column<string>(type: "text", nullable: false),
            Method = t.Column<int>(type: "integer", nullable: false),
            AmountMinor = t.Column<long>(type: "bigint", nullable: false),
            AmountCurrency = t.Column<string>(type: "character varying(3)", nullable: false, maxLength: 3),
            ReceivedUtc = t.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            ReceivedByUserId = t.Column<string>(type: "text", nullable: false),
            CashSessionId = t.Column<string>(type: "text", nullable: true),
            ChequeId = t.Column<string>(type: "text", nullable: true),
            CardReference = t.Column<string>(type: "text", nullable: true),
            OperationId = t.Column<string>(type: "text", nullable: true),
            Settlement = t.Column<int>(type: "integer", nullable: false),
            Note = t.Column<string>(type: "text", nullable: true),
            ReversesPaymentId = t.Column<string>(type: "text", nullable: true)
        }, constraints: t => t.PrimaryKey("PK_Payments", x => x.Id));
        migrationBuilder.CreateIndex(name: "IX_Payments_TenantId_OperationId", schema: "public", table: "Payments", columns: new[] { "TenantId", "OperationId" }, unique: true, filter: "\"OperationId\" IS NOT NULL AND NOT \"Deleted\"");
        migrationBuilder.CreateIndex(name: "IX_Payments_TenantId_InvoiceId", schema: "public", table: "Payments", columns: new[] { "TenantId", "InvoiceId" }, unique: false);
        migrationBuilder.CreateIndex(name: "IX_Payments_TenantId_CashSessionId", schema: "public", table: "Payments", columns: new[] { "TenantId", "CashSessionId" }, unique: false);
        migrationBuilder.CreateIndex(name: "IX_Payments_TenantId_ReceivedUtc_Method", schema: "public", table: "Payments", columns: new[] { "TenantId", "ReceivedUtc", "Method" }, unique: false);
        VersionTrigger.Apply(migrationBuilder, "Payments");
        migrationBuilder.CreateTable(name: "Insurers", schema: "public", columns: t => new
        {
            Id = t.Column<string>(type: "character varying(26)", nullable: false, maxLength: 26),
            TenantId = t.Column<string>(type: "character varying(26)", nullable: false, maxLength: 26),
            UpdatedAt = t.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, precision: 6),
            Version = t.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
            Deleted = t.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
            Code = t.Column<string>(type: "text", nullable: false),
            Name = t.Column<string>(type: "text", nullable: false),
            CoveragePercent = t.Column<decimal>(type: "numeric", nullable: false),
            CeilingMinor = t.Column<long>(type: "bigint", nullable: false)
        }, constraints: t => t.PrimaryKey("PK_Insurers", x => x.Id));
        migrationBuilder.CreateIndex(name: "IX_Insurers_TenantId_Code", schema: "public", table: "Insurers", columns: new[] { "TenantId", "Code" }, unique: true);
        VersionTrigger.Apply(migrationBuilder, "Insurers");
        migrationBuilder.CreateTable(name: "CashSessions", schema: "public", columns: t => new
        {
            Id = t.Column<string>(type: "character varying(26)", nullable: false, maxLength: 26),
            TenantId = t.Column<string>(type: "character varying(26)", nullable: false, maxLength: 26),
            UpdatedAt = t.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, precision: 6),
            Version = t.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
            Deleted = t.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
            SessionNumber = t.Column<string>(type: "text", nullable: false),
            CashierUserId = t.Column<string>(type: "text", nullable: false),
            TerminalCode = t.Column<string>(type: "text", nullable: true),
            State = t.Column<int>(type: "integer", nullable: false),
            OpenedUtc = t.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            ClosedUtc = t.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
            OpeningFloatMinor = t.Column<long>(type: "bigint", nullable: false),
            OpeningFloatCurrency = t.Column<string>(type: "character varying(3)", nullable: false, maxLength: 3),
            DeclaredClosingMinor = t.Column<long>(type: "bigint", nullable: false),
            DeclaredClosingCurrency = t.Column<string>(type: "character varying(3)", nullable: false, maxLength: 3),
            ExpectedClosingMinor = t.Column<long>(type: "bigint", nullable: false),
            ExpectedClosingCurrency = t.Column<string>(type: "character varying(3)", nullable: false, maxLength: 3),
            VarianceMinor = t.Column<long>(type: "bigint", nullable: false),
            VarianceCurrency = t.Column<string>(type: "character varying(3)", nullable: false, maxLength: 3),
            CloseNote = t.Column<string>(type: "text", nullable: true),
            ClosedByUserId = t.Column<string>(type: "text", nullable: true),
            ReconciledByUserId = t.Column<string>(type: "text", nullable: true)
        }, constraints: t => t.PrimaryKey("PK_CashSessions", x => x.Id));
        migrationBuilder.CreateIndex(name: "IX_CashSessions_TenantId_State_OpenedUtc", schema: "public", table: "CashSessions", columns: new[] { "TenantId", "State", "OpenedUtc" }, unique: false);
        migrationBuilder.CreateIndex(name: "IX_CashSessions_TenantId_CashierUserId_Open", schema: "public", table: "CashSessions", columns: new[] { "TenantId", "CashierUserId" }, unique: true, filter: "\"State\" = 0 AND NOT \"Deleted\"");
        VersionTrigger.Apply(migrationBuilder, "CashSessions");
        migrationBuilder.CreateTable(name: "InvoiceLines", schema: "public", columns: t => new
        {
            OwnerId = t.Column<string>(type: "character varying(26)", nullable: false, maxLength: 26),
            LineId = t.Column<string>(type: "character varying(26)", nullable: false, maxLength: 26),
            Kind = t.Column<int>(type: "integer", nullable: false),
            CatalogItemId = t.Column<string>(type: "text", nullable: true),
            ProductId = t.Column<string>(type: "text", nullable: true),
            VariantId = t.Column<string>(type: "text", nullable: true),
            Title = t.Column<string>(type: "text", nullable: false),
            QuantityScaled = t.Column<long>(type: "bigint", nullable: false),
            QuantityUnit = t.Column<string>(type: "character varying(12)", nullable: false, maxLength: 12),
            UnitPriceMinor = t.Column<long>(type: "bigint", nullable: false),
            UnitPriceCurrency = t.Column<string>(type: "character varying(3)", nullable: false, maxLength: 3),
            DiscountPercent = t.Column<decimal>(type: "numeric", nullable: false),
            TaxPercent = t.Column<decimal>(type: "numeric", nullable: false),
            Source = t.Column<int>(type: "integer", nullable: false),
            SourceEntityId = t.Column<string>(type: "text", nullable: true)
        }, constraints: t =>
        {
            t.PrimaryKey("PK_InvoiceLines", x => new { x.OwnerId, x.LineId });
            t.ForeignKey("FK_InvoiceLines_Invoices_OwnerId", x => x.OwnerId, principalSchema: "public", principalTable: "Invoices", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
        });
        migrationBuilder.CreateTable(name: "InvoiceAdjustments", schema: "public", columns: t => new
        {
            OwnerId = t.Column<string>(type: "character varying(26)", nullable: false, maxLength: 26),
            AdjustmentId = t.Column<string>(type: "character varying(26)", nullable: false, maxLength: 26),
            Kind = t.Column<int>(type: "integer", nullable: false),
            AmountMinor = t.Column<long>(type: "bigint", nullable: false),
            AmountCurrency = t.Column<string>(type: "character varying(3)", nullable: false, maxLength: 3),
            Reason = t.Column<string>(type: "text", nullable: false),
            CreatedUtc = t.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            CreatedByUserId = t.Column<string>(type: "text", nullable: false),
            OperationId = t.Column<string>(type: "text", nullable: true),
            ReversesAdjustmentId = t.Column<string>(type: "text", nullable: true)
        }, constraints: t =>
        {
            t.PrimaryKey("PK_InvoiceAdjustments", x => new { x.OwnerId, x.AdjustmentId });
            t.ForeignKey("FK_InvoiceAdjustments_Invoices_OwnerId", x => x.OwnerId, principalSchema: "public", principalTable: "Invoices", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
        });
        migrationBuilder.CreateTable(name: "CashMovements", schema: "public", columns: t => new
        {
            OwnerId = t.Column<string>(type: "character varying(26)", nullable: false, maxLength: 26),
            MovementId = t.Column<string>(type: "character varying(26)", nullable: false, maxLength: 26),
            Kind = t.Column<int>(type: "integer", nullable: false),
            AmountMinor = t.Column<long>(type: "bigint", nullable: false),
            AmountCurrency = t.Column<string>(type: "character varying(3)", nullable: false, maxLength: 3),
            InvoiceId = t.Column<string>(type: "text", nullable: true),
            PaymentId = t.Column<string>(type: "text", nullable: true),
            Reason = t.Column<string>(type: "text", nullable: false),
            OccurredUtc = t.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            RecordedByUserId = t.Column<string>(type: "text", nullable: false),
            OperationId = t.Column<string>(type: "text", nullable: true)
        }, constraints: t =>
        {
            t.PrimaryKey("PK_CashMovements", x => new { x.OwnerId, x.MovementId });
            t.ForeignKey("FK_CashMovements_CashSessions_OwnerId", x => x.OwnerId, principalSchema: "public", principalTable: "CashSessions", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
        });
        migrationBuilder.CreateTable(name: "DocumentCounters", schema: "public", columns: t => new
        {
            TenantId = t.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
            Kind = t.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
            Period = t.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
            Next = t.Column<long>(type: "bigint", nullable: false)
        }, constraints: t => t.PrimaryKey("PK_DocumentCounters", x => new { x.TenantId, x.Kind, x.Period }));
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "CashMovements", schema: "public");
        migrationBuilder.DropTable(name: "InvoiceAdjustments", schema: "public");
        migrationBuilder.DropTable(name: "InvoiceLines", schema: "public");
        migrationBuilder.DropTable(name: "CashSessions", schema: "public");
        migrationBuilder.DropTable(name: "Insurers", schema: "public");
        migrationBuilder.DropTable(name: "Payments", schema: "public");
        migrationBuilder.DropTable(name: "Invoices", schema: "public");
        migrationBuilder.DropTable(name: "DocumentCounters", schema: "public");
    }
}
