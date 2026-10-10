namespace Animora.Persistence.PostgreSql.Replication;

/// <summary>Closed replication surface in dependency order; money/security are never replicated.</summary>
public static class SyncContractCatalog
{
    public static IReadOnlyList<SyncEntityDescriptor> ProjectionOrder { get; } = Array.AsReadOnly<SyncEntityDescriptor>(
    [
        new("Tenant", typeof(Animora.Domain.Identity.Tenant), "Tenants", []),
        new("RoleDefinition", typeof(Animora.Domain.Identity.RoleDefinition), "RoleDefinitions", []),
        new("StaffMembership", typeof(Animora.Domain.Identity.StaffMembership), "StaffMemberships", []),
        new("UnitOfMeasure", typeof(Animora.Domain.Inventory.UnitOfMeasure), "UnitsOfMeasure", []),
        new("ProductCategory", typeof(Animora.Domain.Inventory.ProductCategory), "ProductCategories", []),
        new("Product", typeof(Animora.Domain.Inventory.Product), "Products", [new("ProductVariants", "OwnerId", false)]),
        new("Supplier", typeof(Animora.Domain.Purchasing.Supplier), "Suppliers", []),
        new("Insurer", typeof(Animora.Domain.Sales.Insurer), "Insurers", []),
        new("LedgerAccount", typeof(Animora.Domain.Finance.LedgerAccount), "LedgerAccounts", []),
        new("AccountingPeriod", typeof(Animora.Domain.Finance.AccountingPeriod), "AccountingPeriods", []),
        new("Owner", typeof(Animora.Domain.Patients.Owner), "Owners", []),
        new("Pet", typeof(Animora.Domain.Patients.Pet), "Pets", []),
        new("WeightEntry", typeof(Animora.Domain.Patients.WeightEntry), "WeightEntries", []),
        new("DocumentReference", typeof(Animora.Domain.Patients.DocumentReference), "DocumentReferences", []),
        new("MedicalHistoryEntry", typeof(Animora.Domain.Clinical.MedicalHistoryEntry), "MedicalHistory", []),
        new("Room", typeof(Animora.Domain.Boarding.Room), "Rooms", []),
        new("BoardingServiceDef", typeof(Animora.Domain.Boarding.BoardingServiceDef), "BoardingServiceDefs", []),
        new("BoardingStay", typeof(Animora.Domain.Boarding.BoardingStay), "BoardingStays", [new("BoardingStayItems", "OwnerId", false), new("BoardingHandoffLines", "StayId", true)]),
        new("ReceptionQueueEntry", typeof(Animora.Domain.Scheduling.ReceptionQueueEntry), "ReceptionQueueEntries", []),
        new("Appointment", typeof(Animora.Domain.Scheduling.Appointment), "Appointments", []),
        new("Visit", typeof(Animora.Domain.Clinical.Visit), "Visits", [new("VisitRxLines", "OwnerId", false), new("VisitHandoffLines", "VisitId", true)]),
        new("VaccinationRecord", typeof(Animora.Domain.Clinical.VaccinationRecord), "VaccinationRecords", []),
        new("VaccineCatalogItem", typeof(Animora.Domain.Clinical.VaccineCatalogItem), "VaccineCatalogItems", []),
        new("TreatmentPlan", typeof(Animora.Domain.Clinical.TreatmentPlan), "TreatmentPlans", []),
        new("TreatmentOccurrence", typeof(Animora.Domain.Clinical.TreatmentOccurrence), "TreatmentOccurrences", []),
    ]);
}
