using Animora.Persistence.PostgreSql.Outbox;
using Animora.Persistence.PostgreSql.Replication;
using Animora.Domain.Inventory;
using Animora.Domain.Purchasing;
using Animora.Domain.Finance;
using Animora.Domain.Sales;
using Animora.Domain.Cashier;
using Animora.Domain.Boarding;
using Animora.Persistence.PostgreSql.Boarding;
using Animora.Persistence.PostgreSql.Scheduling;
using Animora.Domain.Patients;
using Animora.Domain.Clinical;
using Animora.Domain.Scheduling;
using Animora.Persistence.PostgreSql.Clinical;
using Animora.Domain.Identity;
using Animora.Persistence.PostgreSql.Commands;
using Animora.Persistence.PostgreSql.Conventions;
using Animora.Persistence.PostgreSql.Identity;
using Animora.Persistence.PostgreSql.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.PostgreSql.Context;

/// <summary>
/// The server's main OLTP context. Tenancy is enforced three ways: named query filters
/// (<see cref="CurrentTenantId"/>), the stamping interceptor on save, and the
/// unnamed-<c>IgnoreQueryFilters</c> ban. The running role has no DDL rights; schema changes
/// ship only as migrations.
/// </summary>
public sealed class AnimoraServerDbContext(DbContextOptions<AnimoraServerDbContext> options, ITenantContext tenantContext)
    : DbContext(options)
{
    /// <summary>
    /// Filter accessor re-bound by EF per query. Throws when no tenant is bound so a missing
    /// tenant can never degrade into a cross-tenant read.
    /// </summary>
    public string CurrentTenantId => tenantContext.TenantId ?? throw new TenantContextMissingException();

    /// <summary>Clinics (tenants).</summary>
    public DbSet<Tenant> Tenants => Set<Tenant>();

    /// <summary>Tenant staff accounts (S04).</summary>
    public DbSet<User> Users => Set<User>();

    /// <summary>User ↔ tenant staff memberships carrying the role (S04).</summary>
    public DbSet<StaffMembership> StaffMemberships => Set<StaffMembership>();

    /// <summary>Per-tenant role → permission definitions (S04).</summary>
    public DbSet<RoleDefinition> RoleDefinitions => Set<RoleDefinition>();

    /// <summary>Registered client installations (S04).</summary>
    public DbSet<Device> Devices => Set<Device>();

    /// <summary>Server-issued refresh tokens (S04; hash only).</summary>
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    /// <summary>Append-only security audit trail (S04).</summary>
    public DbSet<SecurityAuditEntry> SecurityAudit => Set<SecurityAuditEntry>();

    /// <summary>Vendor-global asymmetric signing keys (S05); never tenant-scoped.</summary>
    public DbSet<Security.SigningKey> SigningKeys => Set<Security.SigningKey>();

    /// <summary>Server-side command idempotency store (S06).</summary>
    public DbSet<CommandIntakeRecord> CommandIntake => Set<CommandIntakeRecord>();

    /// <summary>Server ids each command created (S06).</summary>
    public DbSet<CommandIdMapEntry> CommandIdMap => Set<CommandIdMapEntry>();

    public DbSet<Owner> OwnerSet => Set<Owner>();
    public DbSet<Pet> PetSet => Set<Pet>();
    public DbSet<WeightEntry> WeightEntrySet => Set<WeightEntry>();
    public DbSet<MedicalHistoryEntry> MedicalHistoryEntrySet => Set<MedicalHistoryEntry>();
    public DbSet<DocumentReference> DocumentReferenceSet => Set<DocumentReference>();
    public DbSet<ReceptionQueueEntry> ReceptionQueueEntrySet => Set<ReceptionQueueEntry>();
    public DbSet<Visit> VisitSet => Set<Visit>();
    public DbSet<VaccinationRecord> VaccinationRecordSet => Set<VaccinationRecord>();
    public DbSet<TreatmentPlan> TreatmentPlanSet => Set<TreatmentPlan>();
    public DbSet<TreatmentOccurrence> TreatmentOccurrenceSet => Set<TreatmentOccurrence>();
    public DbSet<VaccineCatalogItem> VaccineCatalogItemSet => Set<VaccineCatalogItem>();
    public DbSet<VisitBillingHandoff> VisitBillingHandoffSet => Set<VisitBillingHandoff>();

    public DbSet<Appointment> AppointmentSet => Set<Appointment>();
    public DbSet<AppointmentSettingsEntity> AppointmentSettingsEntitySet => Set<AppointmentSettingsEntity>();
    public DbSet<Room> RoomSet => Set<Room>();
    public DbSet<BoardingServiceDef> BoardingServiceDefSet => Set<BoardingServiceDef>();
    public DbSet<BoardingStay> BoardingStaySet => Set<BoardingStay>();
    public DbSet<BoardingBillingHandoff> BoardingBillingHandoffSet => Set<BoardingBillingHandoff>();

    public DbSet<Invoice> InvoiceSet => Set<Invoice>();
    public DbSet<Payment> PaymentSet => Set<Payment>();
    public DbSet<Insurer> InsurerSet => Set<Insurer>();
    public DbSet<CashSession> CashSessionSet => Set<CashSession>();
    public DbSet<DocumentCounter> DocumentCounters => Set<DocumentCounter>();

    public DbSet<Product> ProductSet => Set<Product>();
    public DbSet<ProductCategory> ProductCategorySet => Set<ProductCategory>();
    public DbSet<StockMovement> StockMovementSet => Set<StockMovement>();
    public DbSet<UnitOfMeasure> UnitOfMeasureSet => Set<UnitOfMeasure>();
    public DbSet<GoodsReceipt> GoodsReceiptSet => Set<GoodsReceipt>();
    public DbSet<PurchaseOrder> PurchaseOrderSet => Set<PurchaseOrder>();
    public DbSet<Supplier> SupplierSet => Set<Supplier>();
    public DbSet<AccountingPeriod> AccountingPeriodSet => Set<AccountingPeriod>();
    public DbSet<Cheque> ChequeSet => Set<Cheque>();
    public DbSet<Expense> ExpenseSet => Set<Expense>();
    public DbSet<JournalEntry> JournalEntrySet => Set<JournalEntry>();
    public DbSet<LedgerAccount> LedgerAccountSet => Set<LedgerAccount>();
    public DbSet<RecurringExpensePayment> RecurringExpensePaymentSet => Set<RecurringExpensePayment>();
    public DbSet<RecurringExpense> RecurringExpenseSet => Set<RecurringExpense>();

    public DbSet<OutboxMessage> ServerOutboxMessages => Set<OutboxMessage>();
    public DbSet<ConsumedMessage> ConsumedMessages => Set<ConsumedMessage>();
    public DbSet<FeedBatchRow> FeedBatches => Set<FeedBatchRow>();
    public DbSet<FeedBatchPageRow> FeedBatchPages => Set<FeedBatchPageRow>();
    public DbSet<TenantFeedStateRow> TenantFeedState => Set<TenantFeedStateRow>();
    public DbSet<TenantJournalRow> TenantJournal => Set<TenantJournalRow>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AnimoraServerDbContext).Assembly);
        ServerModelConventions.ApplyTenancy(modelBuilder, () => CurrentTenantId);

        modelBuilder.Entity<OutboxMessage>().HasQueryFilter(ServerModelConventions.TenantFilterName, e => e.TenantId == CurrentTenantId);
        modelBuilder.Entity<ConsumedMessage>().HasQueryFilter(ServerModelConventions.TenantFilterName, e => e.TenantId == CurrentTenantId);
        modelBuilder.Entity<FeedBatchRow>().HasQueryFilter(ServerModelConventions.TenantFilterName, e => e.TenantId == CurrentTenantId);
        modelBuilder.Entity<FeedBatchPageRow>().HasQueryFilter(ServerModelConventions.TenantFilterName, e => e.TenantId == CurrentTenantId);
        modelBuilder.Entity<TenantFeedStateRow>()
            .HasQueryFilter(ServerModelConventions.TenantFilterName, e => e.TenantId == CurrentTenantId);
        modelBuilder.Entity<TenantJournalRow>()
            .HasQueryFilter(ServerModelConventions.TenantFilterName, e => e.TenantId == CurrentTenantId);

        modelBuilder.Entity<Animora.Persistence.PostgreSql.Licensing.LicenseRecord>()
            .HasQueryFilter(ServerModelConventions.TenantFilterName, e => e.TenantId == CurrentTenantId);

        // RefreshToken / SecurityAuditEntry are not SyncableEntity, so the convention skips them: scope
        // them to the tenant in context here (accessor stays rooted on this context instance).
        modelBuilder.Entity<DocumentCounter>()
            .HasQueryFilter(ServerModelConventions.TenantFilterName, e => e.TenantId == CurrentTenantId);
        modelBuilder.Entity<RefreshToken>()
            .HasQueryFilter(ServerModelConventions.TenantFilterName, e => e.TenantId == CurrentTenantId);
        modelBuilder.Entity<SecurityAuditEntry>()
            .HasQueryFilter(ServerModelConventions.TenantFilterName, e => e.TenantId == CurrentTenantId);
        modelBuilder.Entity<CommandIntakeRecord>()
            .HasQueryFilter(ServerModelConventions.TenantFilterName, e => e.TenantId == CurrentTenantId);
        modelBuilder.Entity<CommandIdMapEntry>()
            .HasQueryFilter(ServerModelConventions.TenantFilterName, e => e.TenantId == CurrentTenantId);
    }
}
