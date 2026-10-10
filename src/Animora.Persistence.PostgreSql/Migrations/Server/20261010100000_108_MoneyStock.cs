using System;
using Animora.Persistence.PostgreSql.Concurrency;
using Microsoft.EntityFrameworkCore.Migrations;
#nullable disable
namespace Animora.Persistence.PostgreSql.Migrations.Server;

// Manually authored under the no-build/no-test execution directive.
public partial class _108_MoneyStock : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(name: "Products", schema: "public", columns: t => new
        {
            Id = t.Column<string>(type: "character varying(26)", nullable: false, maxLength: 26),
            TenantId = t.Column<string>(type: "character varying(26)", nullable: false, maxLength: 26),
            UpdatedAt = t.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, precision: 6),
            Version = t.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
            Deleted = t.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
            Sku = t.Column<string>(type: "text", nullable: false),
            Name = t.Column<string>(type: "text", nullable: false),
            Brand = t.Column<string>(type: "text", nullable: true),
            CategoryId = t.Column<string>(type: "text", nullable: false),
            Kind = t.Column<int>(type: "integer", nullable: false),
            UnitCode = t.Column<string>(type: "text", nullable: false),
            IsPrescriptionOnly = t.Column<bool>(type: "boolean", nullable: false),
            IsActive = t.Column<bool>(type: "boolean", nullable: false),
            TrackStock = t.Column<bool>(type: "boolean", nullable: false),
            ReorderLevelScaled = t.Column<long>(type: "bigint", nullable: false),
            ReorderLevelUnit = t.Column<string>(type: "character varying(12)", nullable: false, maxLength: 12),
            DefaultCostPriceMinor = t.Column<long>(type: "bigint", nullable: false),
            DefaultCostPriceCurrency = t.Column<string>(type: "character varying(3)", nullable: false, maxLength: 3),
            DefaultSalePriceMinor = t.Column<long>(type: "bigint", nullable: false),
            DefaultSalePriceCurrency = t.Column<string>(type: "character varying(3)", nullable: false, maxLength: 3),
            ShelfLifeDays = t.Column<int>(type: "integer", nullable: true),
            Notes = t.Column<string>(type: "text", nullable: true)
        }, constraints: t => t.PrimaryKey("PK_Products", x => x.Id));
        migrationBuilder.CreateIndex(name: "IX_Products_TenantId_Name", schema: "public", table: "Products", columns: new[] { "TenantId", "Name" }, unique: false);
        migrationBuilder.CreateIndex(name: "IX_Products_TenantId_CategoryId", schema: "public", table: "Products", columns: new[] { "TenantId", "CategoryId" }, unique: false);
        migrationBuilder.CreateIndex(name: "IX_Products_TenantId_Sku", schema: "public", table: "Products", columns: new[] { "TenantId", "Sku" }, unique: true, filter: "\"Sku\" IS NOT NULL");
        VersionTrigger.Apply(migrationBuilder, "Products");
        migrationBuilder.CreateTable(name: "ProductCategories", schema: "public", columns: t => new
        {
            Id = t.Column<string>(type: "character varying(26)", nullable: false, maxLength: 26),
            TenantId = t.Column<string>(type: "character varying(26)", nullable: false, maxLength: 26),
            UpdatedAt = t.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, precision: 6),
            Version = t.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
            Deleted = t.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
            Code = t.Column<string>(type: "text", nullable: false),
            DisplayName = t.Column<string>(type: "text", nullable: false),
            ParentCategoryId = t.Column<string>(type: "text", nullable: true),
            SortOrder = t.Column<int>(type: "integer", nullable: false),
            IsActive = t.Column<bool>(type: "boolean", nullable: false)
        }, constraints: t => t.PrimaryKey("PK_ProductCategories", x => x.Id));
        migrationBuilder.CreateIndex(name: "IX_ProductCategories_TenantId_Code", schema: "public", table: "ProductCategories", columns: new[] { "TenantId", "Code" }, unique: true);
        VersionTrigger.Apply(migrationBuilder, "ProductCategories");
        migrationBuilder.CreateTable(name: "UnitsOfMeasure", schema: "public", columns: t => new
        {
            Id = t.Column<string>(type: "character varying(26)", nullable: false, maxLength: 26),
            TenantId = t.Column<string>(type: "character varying(26)", nullable: false, maxLength: 26),
            UpdatedAt = t.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, precision: 6),
            Version = t.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
            Deleted = t.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
            Code = t.Column<string>(type: "text", nullable: false),
            DisplayKey = t.Column<string>(type: "text", nullable: false),
            DecimalPlaces = t.Column<int>(type: "integer", nullable: false),
            BaseUnitCode = t.Column<string>(type: "text", nullable: true),
            ConversionToBase = t.Column<decimal>(type: "numeric", nullable: true)
        }, constraints: t => t.PrimaryKey("PK_UnitsOfMeasure", x => x.Id));
        migrationBuilder.CreateIndex(name: "IX_UnitsOfMeasure_TenantId_Code", schema: "public", table: "UnitsOfMeasure", columns: new[] { "TenantId", "Code" }, unique: true);
        VersionTrigger.Apply(migrationBuilder, "UnitsOfMeasure");
        migrationBuilder.CreateTable(name: "StockMovements", schema: "public", columns: t => new
        {
            Id = t.Column<string>(type: "character varying(26)", nullable: false, maxLength: 26),
            TenantId = t.Column<string>(type: "character varying(26)", nullable: false, maxLength: 26),
            UpdatedAt = t.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, precision: 6),
            Version = t.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
            Deleted = t.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
            ProductId = t.Column<string>(type: "text", nullable: false),
            VariantId = t.Column<string>(type: "text", nullable: false),
            Kind = t.Column<int>(type: "integer", nullable: false),
            QuantityScaled = t.Column<long>(type: "bigint", nullable: false),
            QuantityUnit = t.Column<string>(type: "character varying(12)", nullable: false, maxLength: 12),
            UnitCostMinor = t.Column<long>(type: "bigint", nullable: false),
            UnitCostCurrency = t.Column<string>(type: "character varying(3)", nullable: false, maxLength: 3),
            LotNumber = t.Column<string>(type: "text", nullable: true),
            ExpiryUtc = t.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
            OccurredUtc = t.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            SourceEntityType = t.Column<string>(type: "text", nullable: true),
            SourceEntityId = t.Column<string>(type: "text", nullable: true),
            OperationId = t.Column<string>(type: "text", nullable: true),
            RecordedByUserId = t.Column<string>(type: "text", nullable: false),
            Reason = t.Column<string>(type: "text", nullable: true),
            ReversesMovementId = t.Column<string>(type: "text", nullable: true)
        }, constraints: t => t.PrimaryKey("PK_StockMovements", x => x.Id));
        migrationBuilder.CreateIndex(name: "IX_StockMovements_TenantId_ProductId_VariantId_OccurredUtc", schema: "public", table: "StockMovements", columns: new[] { "TenantId", "ProductId", "VariantId", "OccurredUtc" }, unique: false);
        migrationBuilder.CreateIndex(name: "IX_StockMovements_TenantId_Kind_OccurredUtc", schema: "public", table: "StockMovements", columns: new[] { "TenantId", "Kind", "OccurredUtc" }, unique: false);
        VersionTrigger.Apply(migrationBuilder, "StockMovements");
        migrationBuilder.CreateTable(name: "PurchaseOrders", schema: "public", columns: t => new
        {
            Id = t.Column<string>(type: "character varying(26)", nullable: false, maxLength: 26),
            TenantId = t.Column<string>(type: "character varying(26)", nullable: false, maxLength: 26),
            UpdatedAt = t.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, precision: 6),
            Version = t.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
            Deleted = t.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
            OrderNumber = t.Column<string>(type: "text", nullable: false),
            SupplierId = t.Column<string>(type: "text", nullable: false),
            State = t.Column<int>(type: "integer", nullable: false),
            OrderedUtc = t.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            ExpectedUtc = t.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
            SubtotalMinor = t.Column<long>(type: "bigint", nullable: false),
            SubtotalCurrency = t.Column<string>(type: "character varying(3)", nullable: false, maxLength: 3),
            DiscountTotalMinor = t.Column<long>(type: "bigint", nullable: false),
            DiscountTotalCurrency = t.Column<string>(type: "character varying(3)", nullable: false, maxLength: 3),
            TaxTotalMinor = t.Column<long>(type: "bigint", nullable: false),
            TaxTotalCurrency = t.Column<string>(type: "character varying(3)", nullable: false, maxLength: 3),
            GrandTotalMinor = t.Column<long>(type: "bigint", nullable: false),
            GrandTotalCurrency = t.Column<string>(type: "character varying(3)", nullable: false, maxLength: 3),
            CreatedByUserId = t.Column<string>(type: "text", nullable: false),
            Notes = t.Column<string>(type: "text", nullable: true)
        }, constraints: t => t.PrimaryKey("PK_PurchaseOrders", x => x.Id));
        migrationBuilder.CreateIndex(name: "IX_PurchaseOrders_TenantId_State_OrderedUtc", schema: "public", table: "PurchaseOrders", columns: new[] { "TenantId", "State", "OrderedUtc" }, unique: false);
        VersionTrigger.Apply(migrationBuilder, "PurchaseOrders");
        migrationBuilder.CreateTable(name: "GoodsReceipts", schema: "public", columns: t => new
        {
            Id = t.Column<string>(type: "character varying(26)", nullable: false, maxLength: 26),
            TenantId = t.Column<string>(type: "character varying(26)", nullable: false, maxLength: 26),
            UpdatedAt = t.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, precision: 6),
            Version = t.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
            Deleted = t.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
            ReceiptNumber = t.Column<string>(type: "text", nullable: false),
            PurchaseOrderId = t.Column<string>(type: "text", nullable: true),
            SupplierId = t.Column<string>(type: "text", nullable: false),
            ReceivedUtc = t.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            ReceivedByUserId = t.Column<string>(type: "text", nullable: false),
            InvoiceReference = t.Column<string>(type: "text", nullable: true),
            State = t.Column<int>(type: "integer", nullable: false),
            ReversesReceiptId = t.Column<string>(type: "text", nullable: true)
        }, constraints: t => t.PrimaryKey("PK_GoodsReceipts", x => x.Id));
        migrationBuilder.CreateIndex(name: "IX_GoodsReceipts_TenantId_PurchaseOrderId", schema: "public", table: "GoodsReceipts", columns: new[] { "TenantId", "PurchaseOrderId" }, unique: false);
        VersionTrigger.Apply(migrationBuilder, "GoodsReceipts");
        migrationBuilder.CreateTable(name: "Suppliers", schema: "public", columns: t => new
        {
            Id = t.Column<string>(type: "character varying(26)", nullable: false, maxLength: 26),
            TenantId = t.Column<string>(type: "character varying(26)", nullable: false, maxLength: 26),
            UpdatedAt = t.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, precision: 6),
            Version = t.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
            Deleted = t.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
            Code = t.Column<string>(type: "text", nullable: false),
            Name = t.Column<string>(type: "text", nullable: false),
            ContactPerson = t.Column<string>(type: "text", nullable: true),
            Phone = t.Column<string>(type: "text", nullable: true),
            Email = t.Column<string>(type: "text", nullable: true),
            Address = t.Column<string>(type: "text", nullable: true),
            NationalId = t.Column<string>(type: "text", nullable: true),
            PaymentTermDays = t.Column<int>(type: "integer", nullable: false),
            IsActive = t.Column<bool>(type: "boolean", nullable: false),
            Notes = t.Column<string>(type: "text", nullable: true)
        }, constraints: t => t.PrimaryKey("PK_Suppliers", x => x.Id));
        migrationBuilder.CreateIndex(name: "IX_Suppliers_TenantId_Name", schema: "public", table: "Suppliers", columns: new[] { "TenantId", "Name" }, unique: false);
        VersionTrigger.Apply(migrationBuilder, "Suppliers");
        migrationBuilder.CreateTable(name: "LedgerAccounts", schema: "public", columns: t => new
        {
            Id = t.Column<string>(type: "character varying(26)", nullable: false, maxLength: 26),
            TenantId = t.Column<string>(type: "character varying(26)", nullable: false, maxLength: 26),
            UpdatedAt = t.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, precision: 6),
            Version = t.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
            Deleted = t.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
            Code = t.Column<string>(type: "text", nullable: false),
            Name = t.Column<string>(type: "text", nullable: false),
            Type = t.Column<int>(type: "integer", nullable: false),
            ParentAccountId = t.Column<string>(type: "text", nullable: true),
            IsPostable = t.Column<bool>(type: "boolean", nullable: false),
            IsActive = t.Column<bool>(type: "boolean", nullable: false),
            Description = t.Column<string>(type: "text", nullable: true)
        }, constraints: t => t.PrimaryKey("PK_LedgerAccounts", x => x.Id));
        migrationBuilder.CreateIndex(name: "IX_LedgerAccounts_TenantId_Code", schema: "public", table: "LedgerAccounts", columns: new[] { "TenantId", "Code" }, unique: true);
        VersionTrigger.Apply(migrationBuilder, "LedgerAccounts");
        migrationBuilder.CreateTable(name: "JournalEntries", schema: "public", columns: t => new
        {
            Id = t.Column<string>(type: "character varying(26)", nullable: false, maxLength: 26),
            TenantId = t.Column<string>(type: "character varying(26)", nullable: false, maxLength: 26),
            UpdatedAt = t.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, precision: 6),
            Version = t.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
            Deleted = t.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
            EntryNumber = t.Column<string>(type: "text", nullable: false),
            PostedUtc = t.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            EffectiveUtc = t.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            PeriodId = t.Column<string>(type: "text", nullable: false),
            Source = t.Column<int>(type: "integer", nullable: false),
            SourceEntityType = t.Column<string>(type: "text", nullable: true),
            SourceEntityId = t.Column<string>(type: "text", nullable: true),
            OperationId = t.Column<string>(type: "text", nullable: true),
            Memo = t.Column<string>(type: "text", nullable: false),
            PostedByUserId = t.Column<string>(type: "text", nullable: false),
            ReversesEntryId = t.Column<string>(type: "text", nullable: true),
            TotalDebitMinor = t.Column<long>(type: "bigint", nullable: false),
            TotalDebitCurrency = t.Column<string>(type: "character varying(3)", nullable: false, maxLength: 3)
        }, constraints: t => t.PrimaryKey("PK_JournalEntries", x => x.Id));
        migrationBuilder.CreateIndex(name: "IX_JournalEntries_TenantId_PeriodId_PostedUtc", schema: "public", table: "JournalEntries", columns: new[] { "TenantId", "PeriodId", "PostedUtc" }, unique: false);
        migrationBuilder.CreateIndex(name: "IX_JournalEntries_TenantId_SourceEntityType_SourceEntityId", schema: "public", table: "JournalEntries", columns: new[] { "TenantId", "SourceEntityType", "SourceEntityId" }, unique: false);
        migrationBuilder.CreateIndex(name: "IX_JournalEntries_TenantId_EntryNumber", schema: "public", table: "JournalEntries", columns: new[] { "TenantId", "EntryNumber" }, unique: true);
        VersionTrigger.Apply(migrationBuilder, "JournalEntries");
        migrationBuilder.CreateTable(name: "AccountingPeriods", schema: "public", columns: t => new
        {
            Id = t.Column<string>(type: "character varying(26)", nullable: false, maxLength: 26),
            TenantId = t.Column<string>(type: "character varying(26)", nullable: false, maxLength: 26),
            UpdatedAt = t.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, precision: 6),
            Version = t.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
            Deleted = t.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
            PeriodCode = t.Column<string>(type: "text", nullable: false),
            StartUtc = t.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            EndUtc = t.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            State = t.Column<int>(type: "integer", nullable: false),
            ClosedUtc = t.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
            ClosedByUserId = t.Column<string>(type: "text", nullable: true),
            ClosingEntryId = t.Column<string>(type: "text", nullable: true),
            Note = t.Column<string>(type: "text", nullable: true)
        }, constraints: t => t.PrimaryKey("PK_AccountingPeriods", x => x.Id));
        migrationBuilder.CreateIndex(name: "IX_AccountingPeriods_TenantId_PeriodCode", schema: "public", table: "AccountingPeriods", columns: new[] { "TenantId", "PeriodCode" }, unique: true);
        VersionTrigger.Apply(migrationBuilder, "AccountingPeriods");
        migrationBuilder.CreateTable(name: "Cheques", schema: "public", columns: t => new
        {
            Id = t.Column<string>(type: "character varying(26)", nullable: false, maxLength: 26),
            TenantId = t.Column<string>(type: "character varying(26)", nullable: false, maxLength: 26),
            UpdatedAt = t.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, precision: 6),
            Version = t.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
            Deleted = t.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
            ChequeNumber = t.Column<string>(type: "text", nullable: false),
            Direction = t.Column<int>(type: "integer", nullable: false),
            BankName = t.Column<string>(type: "text", nullable: true),
            BankBranch = t.Column<string>(type: "text", nullable: true),
            AccountNumber = t.Column<string>(type: "text", nullable: true),
            SayadId = t.Column<string>(type: "text", nullable: true),
            AmountMinor = t.Column<long>(type: "bigint", nullable: false),
            AmountCurrency = t.Column<string>(type: "character varying(3)", nullable: false, maxLength: 3),
            DueUtc = t.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            IssuedUtc = t.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            State = t.Column<int>(type: "integer", nullable: false),
            OwnerId = t.Column<string>(type: "character varying(26)", nullable: true, maxLength: 26),
            SupplierId = t.Column<string>(type: "text", nullable: true),
            InvoiceId = t.Column<string>(type: "text", nullable: true),
            PaymentId = t.Column<string>(type: "text", nullable: true),
            JournalEntryId = t.Column<string>(type: "text", nullable: true),
            Note = t.Column<string>(type: "text", nullable: true)
        }, constraints: t => t.PrimaryKey("PK_Cheques", x => x.Id));
        migrationBuilder.CreateIndex(name: "IX_Cheques_TenantId_State_DueUtc", schema: "public", table: "Cheques", columns: new[] { "TenantId", "State", "DueUtc" }, unique: false);
        VersionTrigger.Apply(migrationBuilder, "Cheques");
        migrationBuilder.CreateTable(name: "Expenses", schema: "public", columns: t => new
        {
            Id = t.Column<string>(type: "character varying(26)", nullable: false, maxLength: 26),
            TenantId = t.Column<string>(type: "character varying(26)", nullable: false, maxLength: 26),
            UpdatedAt = t.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, precision: 6),
            Version = t.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
            Deleted = t.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
            ExpenseNumber = t.Column<string>(type: "text", nullable: false),
            CategoryCode = t.Column<string>(type: "text", nullable: false),
            Title = t.Column<string>(type: "text", nullable: false),
            AmountMinor = t.Column<long>(type: "bigint", nullable: false),
            AmountCurrency = t.Column<string>(type: "character varying(3)", nullable: false, maxLength: 3),
            IncurredUtc = t.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            State = t.Column<int>(type: "integer", nullable: false),
            SettledMethod = t.Column<int>(type: "integer", nullable: true),
            SupplierId = t.Column<string>(type: "text", nullable: true),
            ChequeId = t.Column<string>(type: "text", nullable: true),
            ReceiptFileId = t.Column<string>(type: "text", nullable: true),
            JournalEntryId = t.Column<string>(type: "text", nullable: true),
            CreatedByUserId = t.Column<string>(type: "text", nullable: false),
            Note = t.Column<string>(type: "text", nullable: true),
            ReversesExpenseId = t.Column<string>(type: "text", nullable: true)
        }, constraints: t => t.PrimaryKey("PK_Expenses", x => x.Id));
        migrationBuilder.CreateIndex(name: "IX_Expenses_TenantId_IncurredUtc_CategoryCode", schema: "public", table: "Expenses", columns: new[] { "TenantId", "IncurredUtc", "CategoryCode" }, unique: false);
        VersionTrigger.Apply(migrationBuilder, "Expenses");
        migrationBuilder.CreateTable(name: "RecurringExpenses", schema: "public", columns: t => new
        {
            Id = t.Column<string>(type: "character varying(26)", nullable: false, maxLength: 26),
            TenantId = t.Column<string>(type: "character varying(26)", nullable: false, maxLength: 26),
            UpdatedAt = t.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, precision: 6),
            Version = t.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
            Deleted = t.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
            CategoryCode = t.Column<string>(type: "text", nullable: false),
            Title = t.Column<string>(type: "text", nullable: false),
            AmountMinor = t.Column<long>(type: "bigint", nullable: false),
            AmountCurrency = t.Column<string>(type: "character varying(3)", nullable: false, maxLength: 3),
            DayOfMonth = t.Column<int>(type: "integer", nullable: false),
            IsActive = t.Column<bool>(type: "boolean", nullable: false),
            SupplierId = t.Column<string>(type: "text", nullable: true),
            Note = t.Column<string>(type: "text", nullable: true),
            CreatedUtc = t.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
        }, constraints: t => t.PrimaryKey("PK_RecurringExpenses", x => x.Id));
        migrationBuilder.CreateIndex(name: "IX_RecurringExpenses_TenantId_Title", schema: "public", table: "RecurringExpenses", columns: new[] { "TenantId", "Title" }, unique: false);
        VersionTrigger.Apply(migrationBuilder, "RecurringExpenses");
        migrationBuilder.CreateTable(name: "RecurringExpensePayments", schema: "public", columns: t => new
        {
            Id = t.Column<string>(type: "character varying(26)", nullable: false, maxLength: 26),
            TenantId = t.Column<string>(type: "character varying(26)", nullable: false, maxLength: 26),
            UpdatedAt = t.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, precision: 6),
            Version = t.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
            Deleted = t.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
            RecurringExpenseId = t.Column<string>(type: "text", nullable: false),
            PeriodCode = t.Column<string>(type: "text", nullable: false),
            ExpenseId = t.Column<string>(type: "text", nullable: false),
            PaidUtc = t.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
        }, constraints: t => t.PrimaryKey("PK_RecurringExpensePayments", x => x.Id));
        migrationBuilder.CreateIndex(name: "IX_RecurringExpensePayments_TenantId_RecurringExpenseId_PeriodCode", schema: "public", table: "RecurringExpensePayments", columns: new[] { "TenantId", "RecurringExpenseId", "PeriodCode" }, unique: true);
        VersionTrigger.Apply(migrationBuilder, "RecurringExpensePayments");
        migrationBuilder.CreateTable(name: "ProductVariants", schema: "public", columns: t => new
        {
            OwnerId = t.Column<string>(type: "character varying(26)", nullable: false, maxLength: 26),
            VariantId = t.Column<string>(type: "character varying(26)", nullable: false, maxLength: 26),
            Sku = t.Column<string>(type: "text", nullable: false),
            AttributeName = t.Column<string>(type: "text", nullable: false),
            AttributeValue = t.Column<string>(type: "text", nullable: false),
            CostPriceMinor = t.Column<long>(type: "bigint", nullable: false),
            CostPriceCurrency = t.Column<string>(type: "character varying(3)", nullable: false, maxLength: 3),
            SalePriceMinor = t.Column<long>(type: "bigint", nullable: false),
            SalePriceCurrency = t.Column<string>(type: "character varying(3)", nullable: false, maxLength: 3),
            ReorderLevelScaled = t.Column<long>(type: "bigint", nullable: false),
            ReorderLevelUnit = t.Column<string>(type: "character varying(12)", nullable: false, maxLength: 12),
            Barcode = t.Column<string>(type: "text", nullable: true),
            IsActive = t.Column<bool>(type: "boolean", nullable: false)
        }, constraints: t =>
        {
            t.PrimaryKey("PK_ProductVariants", x => new { x.OwnerId, x.VariantId });
            t.ForeignKey("FK_ProductVariants_Products_OwnerId", x => x.OwnerId, principalSchema: "public", principalTable: "Products", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
        });
        migrationBuilder.CreateTable(name: "PurchaseOrderLines", schema: "public", columns: t => new
        {
            OwnerId = t.Column<string>(type: "character varying(26)", nullable: false, maxLength: 26),
            LineId = t.Column<string>(type: "character varying(26)", nullable: false, maxLength: 26),
            ProductId = t.Column<string>(type: "text", nullable: false),
            VariantId = t.Column<string>(type: "text", nullable: false),
            OrderedQuantityScaled = t.Column<long>(type: "bigint", nullable: false),
            OrderedQuantityUnit = t.Column<string>(type: "character varying(12)", nullable: false, maxLength: 12),
            ReceivedQuantityScaled = t.Column<long>(type: "bigint", nullable: false),
            ReceivedQuantityUnit = t.Column<string>(type: "character varying(12)", nullable: false, maxLength: 12),
            UnitCostMinor = t.Column<long>(type: "bigint", nullable: false),
            UnitCostCurrency = t.Column<string>(type: "character varying(3)", nullable: false, maxLength: 3),
            DiscountPercent = t.Column<decimal>(type: "numeric", nullable: false),
            TaxPercent = t.Column<decimal>(type: "numeric", nullable: false),
            LotNumber = t.Column<string>(type: "text", nullable: true),
            ExpiryUtc = t.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
        }, constraints: t =>
        {
            t.PrimaryKey("PK_PurchaseOrderLines", x => new { x.OwnerId, x.LineId });
            t.ForeignKey("FK_PurchaseOrderLines_PurchaseOrders_OwnerId", x => x.OwnerId, principalSchema: "public", principalTable: "PurchaseOrders", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
        });
        migrationBuilder.CreateTable(name: "GoodsReceiptLines", schema: "public", columns: t => new
        {
            OwnerId = t.Column<string>(type: "character varying(26)", nullable: false, maxLength: 26),
            LineId = t.Column<string>(type: "character varying(26)", nullable: false, maxLength: 26),
            ProductId = t.Column<string>(type: "text", nullable: false),
            VariantId = t.Column<string>(type: "text", nullable: false),
            QuantityScaled = t.Column<long>(type: "bigint", nullable: false),
            QuantityUnit = t.Column<string>(type: "character varying(12)", nullable: false, maxLength: 12),
            UnitCostMinor = t.Column<long>(type: "bigint", nullable: false),
            UnitCostCurrency = t.Column<string>(type: "character varying(3)", nullable: false, maxLength: 3),
            LotNumber = t.Column<string>(type: "text", nullable: true),
            ExpiryUtc = t.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
        }, constraints: t =>
        {
            t.PrimaryKey("PK_GoodsReceiptLines", x => new { x.OwnerId, x.LineId });
            t.ForeignKey("FK_GoodsReceiptLines_GoodsReceipts_OwnerId", x => x.OwnerId, principalSchema: "public", principalTable: "GoodsReceipts", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
        });
        migrationBuilder.CreateTable(name: "JournalLines", schema: "public", columns: t => new
        {
            OwnerId = t.Column<string>(type: "character varying(26)", nullable: false, maxLength: 26),
            LineId = t.Column<string>(type: "character varying(26)", nullable: false, maxLength: 26),
            AccountCode = t.Column<string>(type: "text", nullable: false),
            Side = t.Column<int>(type: "integer", nullable: false),
            AmountMinor = t.Column<long>(type: "bigint", nullable: false),
            AmountCurrency = t.Column<string>(type: "character varying(3)", nullable: false, maxLength: 3),
            Memo = t.Column<string>(type: "text", nullable: true),
            CostCenter = t.Column<string>(type: "text", nullable: true)
        }, constraints: t =>
        {
            t.PrimaryKey("PK_JournalLines", x => new { x.OwnerId, x.LineId });
            t.ForeignKey("FK_JournalLines_JournalEntries_OwnerId", x => x.OwnerId, principalSchema: "public", principalTable: "JournalEntries", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
        });
        migrationBuilder.CreateTable(name: "ChequeEvents", schema: "public", columns: t => new
        {
            OwnerId = t.Column<string>(type: "character varying(26)", nullable: false, maxLength: 26),
            EventId = t.Column<string>(type: "character varying(26)", nullable: false, maxLength: 26),
            FromState = t.Column<int>(type: "integer", nullable: false),
            ToState = t.Column<int>(type: "integer", nullable: false),
            OccurredUtc = t.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            ActorUserId = t.Column<string>(type: "text", nullable: false),
            Reason = t.Column<string>(type: "text", nullable: true)
        }, constraints: t =>
        {
            t.PrimaryKey("PK_ChequeEvents", x => new { x.OwnerId, x.EventId });
            t.ForeignKey("FK_ChequeEvents_Cheques_OwnerId", x => x.OwnerId, principalSchema: "public", principalTable: "Cheques", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
        });
        migrationBuilder.Sql("""
            CREATE FUNCTION public.animora_money_stock_immutable() RETURNS trigger LANGUAGE plpgsql AS $fn$
            BEGIN
                RAISE EXCEPTION 'Append-only table % does not allow %', TG_TABLE_NAME, TG_OP USING ERRCODE = '23514';
            END;
            $fn$;
            """);
        migrationBuilder.Sql("CREATE TRIGGER trg_StockMovements_immutable BEFORE UPDATE OR DELETE ON public.\"StockMovements\" FOR EACH ROW EXECUTE FUNCTION public.animora_money_stock_immutable();");
        migrationBuilder.Sql("CREATE TRIGGER trg_JournalEntries_immutable BEFORE UPDATE OR DELETE ON public.\"JournalEntries\" FOR EACH ROW EXECUTE FUNCTION public.animora_money_stock_immutable();");
        migrationBuilder.Sql("CREATE TRIGGER trg_JournalLines_immutable BEFORE UPDATE OR DELETE ON public.\"JournalLines\" FOR EACH ROW EXECUTE FUNCTION public.animora_money_stock_immutable();");
        migrationBuilder.Sql("CREATE TRIGGER trg_GoodsReceipts_immutable BEFORE UPDATE OR DELETE ON public.\"GoodsReceipts\" FOR EACH ROW EXECUTE FUNCTION public.animora_money_stock_immutable();");
        migrationBuilder.Sql("CREATE TRIGGER trg_GoodsReceiptLines_immutable BEFORE UPDATE OR DELETE ON public.\"GoodsReceiptLines\" FOR EACH ROW EXECUTE FUNCTION public.animora_money_stock_immutable();");
        migrationBuilder.Sql("CREATE TRIGGER trg_ChequeEvents_immutable BEFORE UPDATE OR DELETE ON public.\"ChequeEvents\" FOR EACH ROW EXECUTE FUNCTION public.animora_money_stock_immutable();");
        migrationBuilder.Sql("CREATE TRIGGER trg_RecurringExpensePayments_immutable BEFORE UPDATE OR DELETE ON public.\"RecurringExpensePayments\" FOR EACH ROW EXECUTE FUNCTION public.animora_money_stock_immutable();");
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "ChequeEvents", schema: "public");
        migrationBuilder.DropTable(name: "JournalLines", schema: "public");
        migrationBuilder.DropTable(name: "GoodsReceiptLines", schema: "public");
        migrationBuilder.DropTable(name: "PurchaseOrderLines", schema: "public");
        migrationBuilder.DropTable(name: "ProductVariants", schema: "public");
        migrationBuilder.DropTable(name: "RecurringExpensePayments", schema: "public");
        migrationBuilder.DropTable(name: "RecurringExpenses", schema: "public");
        migrationBuilder.DropTable(name: "Expenses", schema: "public");
        migrationBuilder.DropTable(name: "Cheques", schema: "public");
        migrationBuilder.DropTable(name: "AccountingPeriods", schema: "public");
        migrationBuilder.DropTable(name: "JournalEntries", schema: "public");
        migrationBuilder.DropTable(name: "LedgerAccounts", schema: "public");
        migrationBuilder.DropTable(name: "Suppliers", schema: "public");
        migrationBuilder.DropTable(name: "GoodsReceipts", schema: "public");
        migrationBuilder.DropTable(name: "PurchaseOrders", schema: "public");
        migrationBuilder.DropTable(name: "StockMovements", schema: "public");
        migrationBuilder.DropTable(name: "UnitsOfMeasure", schema: "public");
        migrationBuilder.DropTable(name: "ProductCategories", schema: "public");
        migrationBuilder.DropTable(name: "Products", schema: "public");
        migrationBuilder.Sql("DROP FUNCTION public.animora_money_stock_immutable();");
    }
}
