using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Animora.Persistence.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class _002_LocalBusinessSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AccountingPeriods",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    PeriodCode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    StartUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    EndUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    ClosedUtc = table.Column<long>(type: "INTEGER", nullable: true),
                    ClosedByUserId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: true),
                    ClosingEntryId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: true),
                    Note = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    Version = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    Deleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountingPeriods", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CashSessions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    SessionNumber = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    CashierUserId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    TerminalCode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    OpenedUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    ClosedUtc = table.Column<long>(type: "INTEGER", nullable: true),
                    CloseNote = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    ClosedByUserId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: true),
                    ReconciledByUserId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: true),
                    DeclaredClosingCurrency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    DeclaredClosingMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    ExpectedClosingCurrency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    ExpectedClosingMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    OpeningFloatCurrency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    OpeningFloatMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    VarianceCurrency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    VarianceMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    Version = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    Deleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Cheques",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    ChequeNumber = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    Direction = table.Column<int>(type: "INTEGER", nullable: false),
                    BankName = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    BankBranch = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    AccountNumber = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    SayadId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: true),
                    DueUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    IssuedUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: true),
                    SupplierId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: true),
                    InvoiceId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: true),
                    PaymentId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: true),
                    JournalEntryId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: true),
                    Note = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    AmountCurrency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    AmountMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    Version = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    Deleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cheques", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Expenses",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    ExpenseNumber = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    CategoryCode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    IncurredUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    SettledMethod = table.Column<int>(type: "INTEGER", nullable: true),
                    SupplierId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: true),
                    ChequeId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: true),
                    ReceiptFileId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: true),
                    JournalEntryId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: true),
                    CreatedByUserId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    ReversesExpenseId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: true),
                    AmountCurrency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    AmountMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    Version = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    Deleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Expenses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GoodsReceipts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    ReceiptNumber = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    PurchaseOrderId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: true),
                    SupplierId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    ReceivedUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    ReceivedByUserId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    InvoiceReference = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    ReversesReceiptId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: true),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    Version = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    Deleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GoodsReceipts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Invoices",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    InvoiceNumber = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: true),
                    PetId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: true),
                    VisitId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: true),
                    BoardingStayId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: true),
                    GuestName = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    IssuedUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    PostedUtc = table.Column<long>(type: "INTEGER", nullable: true),
                    SettledUtc = table.Column<long>(type: "INTEGER", nullable: true),
                    DiscountKind = table.Column<int>(type: "INTEGER", nullable: false),
                    DiscountValue = table.Column<string>(type: "TEXT", nullable: false),
                    InsurerId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: true),
                    InsurancePercent = table.Column<string>(type: "TEXT", nullable: false),
                    Settlement = table.Column<int>(type: "INTEGER", nullable: false),
                    OperationId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: true),
                    CreatedByUserId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    SettlementErrorCode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    AppliedPaymentOperationIds = table.Column<string>(type: "TEXT", nullable: false),
                    DiscountTotalCurrency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    DiscountTotalMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    DueTotalCurrency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    DueTotalMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    GrandTotalCurrency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    GrandTotalMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    InsuranceCoverageCurrency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    InsuranceCoverageMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    PaidTotalCurrency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    PaidTotalMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    SubtotalCurrency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    SubtotalMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    TaxTotalCurrency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    TaxTotalMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    Version = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    Deleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Invoices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "JournalEntries",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    EntryNumber = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    PostedUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    EffectiveUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    PeriodId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    Source = table.Column<int>(type: "INTEGER", nullable: false),
                    SourceEntityType = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    SourceEntityId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: true),
                    OperationId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: true),
                    Memo = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    PostedByUserId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    ReversesEntryId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: true),
                    TotalDebitCurrency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    TotalDebitMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    Version = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    Deleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JournalEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LedgerAccounts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    ParentAccountId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: true),
                    IsPostable = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    Version = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    Deleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LedgerAccounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Payments",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    InvoiceId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    Method = table.Column<int>(type: "INTEGER", nullable: false),
                    ReceivedUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    ReceivedByUserId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    CashSessionId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: true),
                    ChequeId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: true),
                    CardReference = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    OperationId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: true),
                    Settlement = table.Column<int>(type: "INTEGER", nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    ReversesPaymentId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: true),
                    AmountCurrency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    AmountMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    Version = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    Deleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Payments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProductCategories",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    ParentCategoryId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    Version = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    Deleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductCategories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Products",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    Sku = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Brand = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    CategoryId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    UnitCode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    IsPrescriptionOnly = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    TrackStock = table.Column<bool>(type: "INTEGER", nullable: false),
                    ShelfLifeDays = table.Column<int>(type: "INTEGER", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    DefaultCostPriceCurrency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    DefaultCostPriceMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    DefaultSalePriceCurrency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    DefaultSalePriceMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    ReorderLevelUnit = table.Column<string>(type: "TEXT", maxLength: 12, nullable: false),
                    ReorderLevelValue = table.Column<string>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    Version = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    Deleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Products", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseOrders",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    OrderNumber = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    SupplierId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    OrderedUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    ExpectedUtc = table.Column<long>(type: "INTEGER", nullable: true),
                    CreatedByUserId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    DiscountTotalCurrency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    DiscountTotalMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    GrandTotalCurrency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    GrandTotalMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    SubtotalCurrency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    SubtotalMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    TaxTotalCurrency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    TaxTotalMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    Version = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    Deleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseOrders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SeedMarkers",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    SeedKey = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    SchemaVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeedMarkers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StockMovements",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    ProductId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    VariantId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    LotNumber = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    ExpiryUtc = table.Column<long>(type: "INTEGER", nullable: true),
                    OccurredUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    SourceEntityType = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    SourceEntityId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: true),
                    OperationId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: true),
                    RecordedByUserId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    ReversesMovementId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: true),
                    QuantityUnit = table.Column<string>(type: "TEXT", maxLength: 12, nullable: false),
                    QuantityScaled = table.Column<long>(type: "INTEGER", nullable: false),
                    UnitCostCurrency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    UnitCostMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    Version = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    Deleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockMovements", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Suppliers",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    ContactPerson = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    Phone = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    Email = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    Address = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    NationalId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: true),
                    PaymentTermDays = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    Version = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    Deleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Suppliers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UnitOfMeasures",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    DisplayKey = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    DecimalPlaces = table.Column<int>(type: "INTEGER", nullable: false),
                    BaseUnitCode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    ConversionToBase = table.Column<decimal>(type: "TEXT", nullable: true),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    Version = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    Deleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UnitOfMeasures", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CashMovements",
                columns: table => new
                {
                    MovementId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    InvoiceId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: true),
                    PaymentId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: true),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    OccurredUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    RecordedByUserId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    OperationId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: true),
                    AmountCurrency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    AmountMinor = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashMovements", x => new { x.OwnerId, x.MovementId });
                    table.ForeignKey(
                        name: "FK_CashMovements_CashSessions_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "CashSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ChequeEvents",
                columns: table => new
                {
                    EventId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    FromState = table.Column<int>(type: "INTEGER", nullable: false),
                    ToState = table.Column<int>(type: "INTEGER", nullable: false),
                    OccurredUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    ActorUserId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChequeEvents", x => new { x.OwnerId, x.EventId });
                    table.ForeignKey(
                        name: "FK_ChequeEvents_Cheques_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "Cheques",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GoodsReceiptLines",
                columns: table => new
                {
                    LineId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    ProductId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    VariantId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    LotNumber = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    ExpiryUtc = table.Column<long>(type: "INTEGER", nullable: true),
                    QuantityUnit = table.Column<string>(type: "TEXT", maxLength: 12, nullable: false),
                    QuantityScaled = table.Column<long>(type: "INTEGER", nullable: false),
                    UnitCostCurrency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    UnitCostMinor = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GoodsReceiptLines", x => new { x.OwnerId, x.LineId });
                    table.ForeignKey(
                        name: "FK_GoodsReceiptLines_GoodsReceipts_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "GoodsReceipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InvoiceAdjustments",
                columns: table => new
                {
                    AdjustmentId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    CreatedUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    OperationId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: true),
                    ReversesAdjustmentId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: true),
                    AmountCurrency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    AmountMinor = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceAdjustments", x => new { x.OwnerId, x.AdjustmentId });
                    table.ForeignKey(
                        name: "FK_InvoiceAdjustments_Invoices_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "Invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InvoiceLines",
                columns: table => new
                {
                    LineId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    CatalogItemId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: true),
                    ProductId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: true),
                    VariantId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    DiscountPercent = table.Column<string>(type: "TEXT", nullable: false),
                    TaxPercent = table.Column<string>(type: "TEXT", nullable: false),
                    Source = table.Column<int>(type: "INTEGER", nullable: false),
                    SourceEntityId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: true),
                    QuantityUnit = table.Column<string>(type: "TEXT", maxLength: 12, nullable: false),
                    QuantityScaled = table.Column<long>(type: "INTEGER", nullable: false),
                    UnitPriceCurrency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    UnitPriceMinor = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceLines", x => new { x.OwnerId, x.LineId });
                    table.ForeignKey(
                        name: "FK_InvoiceLines_Invoices_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "Invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "JournalLines",
                columns: table => new
                {
                    LineId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    AccountCode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Side = table.Column<int>(type: "INTEGER", nullable: false),
                    Memo = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    CostCenter = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    AmountCurrency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    AmountMinor = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JournalLines", x => new { x.OwnerId, x.LineId });
                    table.ForeignKey(
                        name: "FK_JournalLines_JournalEntries_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "JournalEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductVariants",
                columns: table => new
                {
                    VariantId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    Sku = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    AttributeName = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    AttributeValue = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    Barcode = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CostPriceCurrency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    CostPriceMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    ReorderLevelUnit = table.Column<string>(type: "TEXT", maxLength: 12, nullable: false),
                    ReorderLevelValue = table.Column<string>(type: "TEXT", nullable: false),
                    SalePriceCurrency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    SalePriceMinor = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductVariants", x => new { x.OwnerId, x.VariantId });
                    table.ForeignKey(
                        name: "FK_ProductVariants_Products_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseOrderLines",
                columns: table => new
                {
                    LineId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    ProductId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    VariantId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    DiscountPercent = table.Column<string>(type: "TEXT", nullable: false),
                    TaxPercent = table.Column<string>(type: "TEXT", nullable: false),
                    LotNumber = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    ExpiryUtc = table.Column<long>(type: "INTEGER", nullable: true),
                    OrderedQuantityUnit = table.Column<string>(type: "TEXT", maxLength: 12, nullable: false),
                    OrderedQuantityScaled = table.Column<long>(type: "INTEGER", nullable: false),
                    ReceivedQuantityUnit = table.Column<string>(type: "TEXT", maxLength: 12, nullable: false),
                    ReceivedQuantityScaled = table.Column<long>(type: "INTEGER", nullable: false),
                    UnitCostCurrency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    UnitCostMinor = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseOrderLines", x => new { x.OwnerId, x.LineId });
                    table.ForeignKey(
                        name: "FK_PurchaseOrderLines_PurchaseOrders_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "PurchaseOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccountingPeriods_TenantId_PeriodCode",
                table: "AccountingPeriods",
                columns: new[] { "TenantId", "PeriodCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccountingPeriods_TenantId_UpdatedAt",
                table: "AccountingPeriods",
                columns: new[] { "TenantId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CashSessions_TenantId_State_OpenedUtc",
                table: "CashSessions",
                columns: new[] { "TenantId", "State", "OpenedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CashSessions_TenantId_UpdatedAt",
                table: "CashSessions",
                columns: new[] { "TenantId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Cheques_TenantId_State_DueUtc",
                table: "Cheques",
                columns: new[] { "TenantId", "State", "DueUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Cheques_TenantId_UpdatedAt",
                table: "Cheques",
                columns: new[] { "TenantId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_TenantId_IncurredUtc_CategoryCode",
                table: "Expenses",
                columns: new[] { "TenantId", "IncurredUtc", "CategoryCode" });

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_TenantId_UpdatedAt",
                table: "Expenses",
                columns: new[] { "TenantId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_GoodsReceipts_TenantId_PurchaseOrderId",
                table: "GoodsReceipts",
                columns: new[] { "TenantId", "PurchaseOrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_GoodsReceipts_TenantId_UpdatedAt",
                table: "GoodsReceipts",
                columns: new[] { "TenantId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_TenantId_Number",
                table: "Invoices",
                columns: new[] { "TenantId", "InvoiceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_TenantId_OwnerId",
                table: "Invoices",
                columns: new[] { "TenantId", "OwnerId" });

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_TenantId_State_IssuedUtc",
                table: "Invoices",
                columns: new[] { "TenantId", "State", "IssuedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_TenantId_UpdatedAt",
                table: "Invoices",
                columns: new[] { "TenantId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_TenantId_VisitId",
                table: "Invoices",
                columns: new[] { "TenantId", "VisitId" });

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_TenantId_EntryNumber",
                table: "JournalEntries",
                columns: new[] { "TenantId", "EntryNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_TenantId_PeriodId_PostedUtc",
                table: "JournalEntries",
                columns: new[] { "TenantId", "PeriodId", "PostedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_TenantId_SourceEntityType_SourceEntityId",
                table: "JournalEntries",
                columns: new[] { "TenantId", "SourceEntityType", "SourceEntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_TenantId_UpdatedAt",
                table: "JournalEntries",
                columns: new[] { "TenantId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_JournalLines_AccountCode",
                table: "JournalLines",
                column: "AccountCode");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerAccounts_TenantId_Code",
                table: "LedgerAccounts",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LedgerAccounts_TenantId_UpdatedAt",
                table: "LedgerAccounts",
                columns: new[] { "TenantId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Payments_TenantId_InvoiceId",
                table: "Payments",
                columns: new[] { "TenantId", "InvoiceId" });

            migrationBuilder.CreateIndex(
                name: "IX_Payments_TenantId_ReceivedUtc_Method",
                table: "Payments",
                columns: new[] { "TenantId", "ReceivedUtc", "Method" });

            migrationBuilder.CreateIndex(
                name: "IX_Payments_TenantId_UpdatedAt",
                table: "Payments",
                columns: new[] { "TenantId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductCategories_TenantId_UpdatedAt",
                table: "ProductCategories",
                columns: new[] { "TenantId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Products_TenantId_CategoryId",
                table: "Products",
                columns: new[] { "TenantId", "CategoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_Products_TenantId_Name",
                table: "Products",
                columns: new[] { "TenantId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_Products_TenantId_Sku",
                table: "Products",
                columns: new[] { "TenantId", "Sku" },
                unique: true,
                filter: "\"Sku\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Products_TenantId_UpdatedAt",
                table: "Products",
                columns: new[] { "TenantId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_TenantId_State_OrderedUtc",
                table: "PurchaseOrders",
                columns: new[] { "TenantId", "State", "OrderedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_TenantId_UpdatedAt",
                table: "PurchaseOrders",
                columns: new[] { "TenantId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SeedMarkers_SeedKey",
                table: "SeedMarkers",
                column: "SeedKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_TenantId_Kind_OccurredUtc",
                table: "StockMovements",
                columns: new[] { "TenantId", "Kind", "OccurredUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_TenantId_ProductId_VariantId_OccurredUtc",
                table: "StockMovements",
                columns: new[] { "TenantId", "ProductId", "VariantId", "OccurredUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_TenantId_UpdatedAt",
                table: "StockMovements",
                columns: new[] { "TenantId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Suppliers_TenantId_Name",
                table: "Suppliers",
                columns: new[] { "TenantId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_Suppliers_TenantId_UpdatedAt",
                table: "Suppliers",
                columns: new[] { "TenantId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_UnitOfMeasures_TenantId_UpdatedAt",
                table: "UnitOfMeasures",
                columns: new[] { "TenantId", "UpdatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccountingPeriods");

            migrationBuilder.DropTable(
                name: "CashMovements");

            migrationBuilder.DropTable(
                name: "ChequeEvents");

            migrationBuilder.DropTable(
                name: "Expenses");

            migrationBuilder.DropTable(
                name: "GoodsReceiptLines");

            migrationBuilder.DropTable(
                name: "InvoiceAdjustments");

            migrationBuilder.DropTable(
                name: "InvoiceLines");

            migrationBuilder.DropTable(
                name: "JournalLines");

            migrationBuilder.DropTable(
                name: "LedgerAccounts");

            migrationBuilder.DropTable(
                name: "Payments");

            migrationBuilder.DropTable(
                name: "ProductCategories");

            migrationBuilder.DropTable(
                name: "ProductVariants");

            migrationBuilder.DropTable(
                name: "PurchaseOrderLines");

            migrationBuilder.DropTable(
                name: "SeedMarkers");

            migrationBuilder.DropTable(
                name: "StockMovements");

            migrationBuilder.DropTable(
                name: "Suppliers");

            migrationBuilder.DropTable(
                name: "UnitOfMeasures");

            migrationBuilder.DropTable(
                name: "CashSessions");

            migrationBuilder.DropTable(
                name: "Cheques");

            migrationBuilder.DropTable(
                name: "GoodsReceipts");

            migrationBuilder.DropTable(
                name: "Invoices");

            migrationBuilder.DropTable(
                name: "JournalEntries");

            migrationBuilder.DropTable(
                name: "Products");

            migrationBuilder.DropTable(
                name: "PurchaseOrders");
        }
    }
}
