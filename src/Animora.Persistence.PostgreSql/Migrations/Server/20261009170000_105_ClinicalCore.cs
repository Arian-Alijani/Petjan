using System;
using Animora.Persistence.PostgreSql.Concurrency;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace Animora.Persistence.PostgreSql.Migrations.Server;

/// <summary>Additive clinical schema; authored without invoking a build.</summary>
public partial class _105_ClinicalCore : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(name: "Owners", schema: "public", columns: t => new
        {
                Id = t.Column<string>(type: "character varying(26)", nullable: false, maxLength: 26),
                TenantId = t.Column<string>(type: "character varying(26)", nullable: false, maxLength: 26),
                UpdatedAt = t.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, precision: 6),
                Version = t.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                Deleted = t.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                FullName = t.Column<string>(type: "text", nullable: false),
                PhonePrimary = t.Column<string>(type: "text", nullable: false),
                PhoneSecondary = t.Column<string>(type: "text", nullable: true),
                NationalId = t.Column<string>(type: "text", nullable: true),
                Email = t.Column<string>(type: "text", nullable: true),
                Address = t.Column<string>(type: "text", nullable: true),
                City = t.Column<string>(type: "text", nullable: true),
                Gender = t.Column<int>(type: "integer", nullable: false),
                Notes = t.Column<string>(type: "text", nullable: true),
                RegisteredUtc = t.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                IsBlocked = t.Column<bool>(type: "boolean", nullable: false),
                BlockReason = t.Column<string>(type: "text", nullable: true),
                SearchKey = t.Column<string>(type: "text", nullable: false)
        }, constraints: t => t.PrimaryKey("PK_Owners", x => x.Id));
        migrationBuilder.CreateIndex(name: "IX_Owners_TenantId_Phone", schema: "public", table: "Owners", columns: new[] { "TenantId", "PhonePrimary" }, unique: false);
        migrationBuilder.CreateIndex(name: "IX_Owners_TenantId_FullName", schema: "public", table: "Owners", columns: new[] { "TenantId", "FullName" }, unique: false);
        migrationBuilder.CreateIndex(name: "IX_Owners_TenantId_SearchKey", schema: "public", table: "Owners", columns: new[] { "TenantId", "SearchKey" }, unique: false);
        migrationBuilder.CreateIndex(name: "IX_Owners_TenantId_RegisteredUtc", schema: "public", table: "Owners", columns: new[] { "TenantId", "RegisteredUtc" }, unique: false);
        VersionTrigger.Apply(migrationBuilder, "Owners");
        migrationBuilder.CreateTable(name: "Pets", schema: "public", columns: t => new
        {
                Id = t.Column<string>(type: "character varying(26)", nullable: false, maxLength: 26),
                TenantId = t.Column<string>(type: "character varying(26)", nullable: false, maxLength: 26),
                UpdatedAt = t.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, precision: 6),
                Version = t.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                Deleted = t.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                OwnerId = t.Column<string>(type: "text", nullable: false),
                Name = t.Column<string>(type: "text", nullable: false),
                Species = t.Column<int>(type: "integer", nullable: false),
                Breed = t.Column<string>(type: "text", nullable: true),
                Gender = t.Column<int>(type: "integer", nullable: false),
                BirthDateUtc = t.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                IsNeutered = t.Column<bool>(type: "boolean", nullable: false),
                Color = t.Column<string>(type: "text", nullable: true),
                MicrochipCode = t.Column<string>(type: "text", nullable: true),
                MicrochipImplantUtc = t.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                Status = t.Column<int>(type: "integer", nullable: false),
                AllergyNotes = t.Column<string>(type: "text", nullable: true),
                ChronicNotes = t.Column<string>(type: "text", nullable: true),
                RegisteredUtc = t.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                SearchKey = t.Column<string>(type: "text", nullable: false)
        }, constraints: t => t.PrimaryKey("PK_Pets", x => x.Id));
        migrationBuilder.CreateIndex(name: "IX_Pets_TenantId_OwnerId", schema: "public", table: "Pets", columns: new[] { "TenantId", "OwnerId" }, unique: false);
        migrationBuilder.CreateIndex(name: "IX_Pets_TenantId_Name", schema: "public", table: "Pets", columns: new[] { "TenantId", "Name" }, unique: false);
        migrationBuilder.CreateIndex(name: "IX_Pets_TenantId_SearchKey", schema: "public", table: "Pets", columns: new[] { "TenantId", "SearchKey" }, unique: false);
        migrationBuilder.CreateIndex(name: "IX_Pets_TenantId_BirthDateUtc", schema: "public", table: "Pets", columns: new[] { "TenantId", "BirthDateUtc" }, unique: false);
        VersionTrigger.Apply(migrationBuilder, "Pets");
        migrationBuilder.CreateTable(name: "WeightEntries", schema: "public", columns: t => new
        {
                Id = t.Column<string>(type: "character varying(26)", nullable: false, maxLength: 26),
                TenantId = t.Column<string>(type: "character varying(26)", nullable: false, maxLength: 26),
                UpdatedAt = t.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, precision: 6),
                Version = t.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                Deleted = t.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                PetId = t.Column<string>(type: "text", nullable: false),
                WeightKg = t.Column<decimal>(type: "numeric", nullable: false),
                MeasuredUtc = t.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                Note = t.Column<string>(type: "text", nullable: true),
                RecordedByUserId = t.Column<string>(type: "text", nullable: false),
                CorrectsEntryId = t.Column<string>(type: "text", nullable: true)
        }, constraints: t => t.PrimaryKey("PK_WeightEntries", x => x.Id));
        migrationBuilder.CreateIndex(name: "IX_WeightEntries_TenantId_PetId_MeasuredUtc", schema: "public", table: "WeightEntries", columns: new[] { "TenantId", "PetId", "MeasuredUtc" }, unique: false);
        VersionTrigger.Apply(migrationBuilder, "WeightEntries");
        migrationBuilder.CreateTable(name: "DocumentReferences", schema: "public", columns: t => new
        {
                Id = t.Column<string>(type: "character varying(26)", nullable: false, maxLength: 26),
                TenantId = t.Column<string>(type: "character varying(26)", nullable: false, maxLength: 26),
                UpdatedAt = t.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, precision: 6),
                Version = t.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                Deleted = t.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                OwnerId = t.Column<string>(type: "text", nullable: true),
                PetId = t.Column<string>(type: "text", nullable: true),
                VisitId = t.Column<string>(type: "text", nullable: true),
                Title = t.Column<string>(type: "text", nullable: false),
                Kind = t.Column<int>(type: "integer", nullable: false),
                FileId = t.Column<string>(type: "text", nullable: false),
                SizeBytes = t.Column<long>(type: "bigint", nullable: false),
                ContentHashSha256 = t.Column<string>(type: "text", nullable: false),
                UploadedUtc = t.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UploadedByUserId = t.Column<string>(type: "text", nullable: false)
        }, constraints: t => t.PrimaryKey("PK_DocumentReferences", x => x.Id));
        migrationBuilder.CreateIndex(name: "IX_DocumentReferences_TenantId_PetId", schema: "public", table: "DocumentReferences", columns: new[] { "TenantId", "PetId" }, unique: false);
        VersionTrigger.Apply(migrationBuilder, "DocumentReferences");
        migrationBuilder.CreateTable(name: "MedicalHistory", schema: "public", columns: t => new
        {
                Id = t.Column<string>(type: "character varying(26)", nullable: false, maxLength: 26),
                TenantId = t.Column<string>(type: "character varying(26)", nullable: false, maxLength: 26),
                UpdatedAt = t.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, precision: 6),
                Version = t.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                Deleted = t.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                PetId = t.Column<string>(type: "text", nullable: false),
                Kind = t.Column<int>(type: "integer", nullable: false),
                Title = t.Column<string>(type: "text", nullable: false),
                Detail = t.Column<string>(type: "text", nullable: true),
                OccurredUtc = t.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                SourceEntityId = t.Column<string>(type: "text", nullable: true),
                SourceEntityType = t.Column<string>(type: "text", nullable: true),
                RecordedByUserId = t.Column<string>(type: "text", nullable: false)
        }, constraints: t => t.PrimaryKey("PK_MedicalHistory", x => x.Id));
        migrationBuilder.CreateIndex(name: "IX_MedicalHistoryEntries_TenantId_PetId_OccurredUtc", schema: "public", table: "MedicalHistory", columns: new[] { "TenantId", "PetId", "OccurredUtc" }, unique: false);
        migrationBuilder.CreateIndex(name: "IX_MedicalHistoryEntries_TenantId_SourceEntityId", schema: "public", table: "MedicalHistory", columns: new[] { "TenantId", "SourceEntityId" }, unique: false);
        VersionTrigger.Apply(migrationBuilder, "MedicalHistory");
        migrationBuilder.CreateTable(name: "ReceptionQueueEntries", schema: "public", columns: t => new
        {
                Id = t.Column<string>(type: "character varying(26)", nullable: false, maxLength: 26),
                TenantId = t.Column<string>(type: "character varying(26)", nullable: false, maxLength: 26),
                UpdatedAt = t.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, precision: 6),
                Version = t.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                Deleted = t.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                PetId = t.Column<string>(type: "text", nullable: false),
                OwnerId = t.Column<string>(type: "text", nullable: false),
                AppointmentId = t.Column<string>(type: "text", nullable: true),
                DoctorUserId = t.Column<string>(type: "text", nullable: true),
                State = t.Column<int>(type: "integer", nullable: false),
                Source = t.Column<int>(type: "integer", nullable: false),
                TicketNumber = t.Column<int>(type: "integer", nullable: false),
                EnqueuedUtc = t.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CalledUtc = t.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                ClosedUtc = t.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                Priority = t.Column<int>(type: "integer", nullable: false),
                Note = t.Column<string>(type: "text", nullable: true),
                VisitId = t.Column<string>(type: "text", nullable: true)
        }, constraints: t => t.PrimaryKey("PK_ReceptionQueueEntries", x => x.Id));
        migrationBuilder.CreateIndex(name: "IX_ReceptionQueueEntries_TenantId_State_EnqueuedUtc", schema: "public", table: "ReceptionQueueEntries", columns: new[] { "TenantId", "State", "EnqueuedUtc" }, unique: false);
        migrationBuilder.CreateIndex(name: "IX_ReceptionQueueEntries_TenantId_AppointmentId", schema: "public", table: "ReceptionQueueEntries", columns: new[] { "TenantId", "AppointmentId" }, unique: false, filter: "\"AppointmentId\" IS NOT NULL");
        VersionTrigger.Apply(migrationBuilder, "ReceptionQueueEntries");
        migrationBuilder.CreateTable(name: "Visits", schema: "public", columns: t => new
        {
                Id = t.Column<string>(type: "character varying(26)", nullable: false, maxLength: 26),
                TenantId = t.Column<string>(type: "character varying(26)", nullable: false, maxLength: 26),
                UpdatedAt = t.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, precision: 6),
                Version = t.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                Deleted = t.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                PetId = t.Column<string>(type: "text", nullable: false),
                OwnerId = t.Column<string>(type: "text", nullable: false),
                AppointmentId = t.Column<string>(type: "text", nullable: true),
                DoctorUserId = t.Column<string>(type: "text", nullable: false),
                State = t.Column<int>(type: "integer", nullable: false),
                Reason = t.Column<int>(type: "integer", nullable: false),
                ChiefComplaint = t.Column<string>(type: "text", nullable: true),
                StartedUtc = t.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CompletedUtc = t.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                Diagnosis = t.Column<string>(type: "text", nullable: true),
                TreatmentPlan = t.Column<string>(type: "text", nullable: true),
                DoctorNotes = t.Column<string>(type: "text", nullable: true),
                InvoiceId = t.Column<string>(type: "text", nullable: true),
                CancellationReason = t.Column<string>(type: "text", nullable: true),
                ServiceLines = t.Column<string>(type: "jsonb", nullable: false),
                Vitals_WeightKg = t.Column<decimal>(type: "numeric", nullable: true),
                Vitals_TemperatureC = t.Column<decimal>(type: "numeric", nullable: true),
                Vitals_HeartRateBpm = t.Column<int>(type: "integer", nullable: true),
                Vitals_RespiratoryRate = t.Column<int>(type: "integer", nullable: true),
                Vitals_MucousMembrane = t.Column<string>(type: "text", nullable: true),
                Vitals_Hydration = t.Column<string>(type: "text", nullable: true)
        }, constraints: t => t.PrimaryKey("PK_Visits", x => x.Id));
        migrationBuilder.CreateIndex(name: "IX_Visits_TenantId_PetId_StartedUtc", schema: "public", table: "Visits", columns: new[] { "TenantId", "PetId", "StartedUtc" }, unique: false);
        migrationBuilder.CreateIndex(name: "IX_Visits_TenantId_State_StartedUtc", schema: "public", table: "Visits", columns: new[] { "TenantId", "State", "StartedUtc" }, unique: false);
        migrationBuilder.CreateIndex(name: "IX_Visits_TenantId_DoctorUserId_CompletedUtc", schema: "public", table: "Visits", columns: new[] { "TenantId", "DoctorUserId", "CompletedUtc" }, unique: false);
        VersionTrigger.Apply(migrationBuilder, "Visits");
        migrationBuilder.CreateTable(name: "VaccinationRecords", schema: "public", columns: t => new
        {
                Id = t.Column<string>(type: "character varying(26)", nullable: false, maxLength: 26),
                TenantId = t.Column<string>(type: "character varying(26)", nullable: false, maxLength: 26),
                UpdatedAt = t.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, precision: 6),
                Version = t.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                Deleted = t.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                PetId = t.Column<string>(type: "text", nullable: false),
                VaccineCode = t.Column<string>(type: "text", nullable: false),
                VaccineName = t.Column<string>(type: "text", nullable: false),
                DoseNumber = t.Column<int>(type: "integer", nullable: false),
                TotalDoses = t.Column<int>(type: "integer", nullable: true),
                AdministeredUtc = t.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                AdministeredByUserId = t.Column<string>(type: "text", nullable: false),
                BatchNumber = t.Column<string>(type: "text", nullable: true),
                NextDueUtc = t.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                State = t.Column<int>(type: "integer", nullable: false),
                VisitId = t.Column<string>(type: "text", nullable: true),
                Notes = t.Column<string>(type: "text", nullable: true)
        }, constraints: t => t.PrimaryKey("PK_VaccinationRecords", x => x.Id));
        migrationBuilder.CreateIndex(name: "IX_VaccinationRecords_TenantId_DueUtc_State", schema: "public", table: "VaccinationRecords", columns: new[] { "TenantId", "NextDueUtc", "State" }, unique: false);
        migrationBuilder.CreateIndex(name: "IX_VaccinationRecords_TenantId_AdministeredUtc", schema: "public", table: "VaccinationRecords", columns: new[] { "TenantId", "AdministeredUtc" }, unique: false);
        VersionTrigger.Apply(migrationBuilder, "VaccinationRecords");
        migrationBuilder.CreateTable(name: "TreatmentPlans", schema: "public", columns: t => new
        {
                Id = t.Column<string>(type: "character varying(26)", nullable: false, maxLength: 26),
                TenantId = t.Column<string>(type: "character varying(26)", nullable: false, maxLength: 26),
                UpdatedAt = t.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, precision: 6),
                Version = t.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                Deleted = t.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                PetId = t.Column<string>(type: "text", nullable: false),
                ItemType = t.Column<int>(type: "integer", nullable: false),
                Kind = t.Column<int>(type: "integer", nullable: false),
                Title = t.Column<string>(type: "text", nullable: false),
                VaccineCode = t.Column<string>(type: "text", nullable: true),
                Dosage = t.Column<string>(type: "text", nullable: true),
                Notes = t.Column<string>(type: "text", nullable: true),
                NotifyOwner = t.Column<bool>(type: "boolean", nullable: false),
                ScheduleType = t.Column<int>(type: "integer", nullable: false),
                CreatedByUserId = t.Column<string>(type: "text", nullable: false),
                CreatedUtc = t.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                State = t.Column<int>(type: "integer", nullable: false)
        }, constraints: t => t.PrimaryKey("PK_TreatmentPlans", x => x.Id));
        migrationBuilder.CreateIndex(name: "IX_TreatmentPlans_TenantId_PetId_State", schema: "public", table: "TreatmentPlans", columns: new[] { "TenantId", "PetId", "State" }, unique: false);
        VersionTrigger.Apply(migrationBuilder, "TreatmentPlans");
        migrationBuilder.CreateTable(name: "TreatmentOccurrences", schema: "public", columns: t => new
        {
                Id = t.Column<string>(type: "character varying(26)", nullable: false, maxLength: 26),
                TenantId = t.Column<string>(type: "character varying(26)", nullable: false, maxLength: 26),
                UpdatedAt = t.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, precision: 6),
                Version = t.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                Deleted = t.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                PlanId = t.Column<string>(type: "text", nullable: false),
                DueDay = t.Column<DateOnly>(type: "date", nullable: false),
                SortOrder = t.Column<int>(type: "integer", nullable: false),
                State = t.Column<int>(type: "integer", nullable: false),
                DoneUtc = t.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                DoneByUserId = t.Column<string>(type: "text", nullable: true)
        }, constraints: t => t.PrimaryKey("PK_TreatmentOccurrences", x => x.Id));
        migrationBuilder.CreateIndex(name: "IX_TreatmentOccurrences_TenantId_DueDay_State", schema: "public", table: "TreatmentOccurrences", columns: new[] { "TenantId", "DueDay", "State" }, unique: false);
        migrationBuilder.CreateIndex(name: "IX_TreatmentOccurrences_TenantId_PlanId", schema: "public", table: "TreatmentOccurrences", columns: new[] { "TenantId", "PlanId" }, unique: false);
        VersionTrigger.Apply(migrationBuilder, "TreatmentOccurrences");
        migrationBuilder.CreateTable(name: "VaccineCatalogItems", schema: "public", columns: t => new
        {
                Id = t.Column<string>(type: "character varying(26)", nullable: false, maxLength: 26),
                TenantId = t.Column<string>(type: "character varying(26)", nullable: false, maxLength: 26),
                UpdatedAt = t.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, precision: 6),
                Version = t.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                Deleted = t.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                Code = t.Column<string>(type: "text", nullable: false),
                Name = t.Column<string>(type: "text", nullable: false),
                Protects = t.Column<string>(type: "text", nullable: true),
                TotalDoses = t.Column<int>(type: "integer", nullable: false),
                BoosterDays = t.Column<int>(type: "integer", nullable: false),
                Stock = t.Column<int>(type: "integer", nullable: false),
                MinStock = t.Column<int>(type: "integer", nullable: false),
                IsActive = t.Column<bool>(type: "boolean", nullable: false)
        }, constraints: t => t.PrimaryKey("PK_VaccineCatalogItems", x => x.Id));
        migrationBuilder.CreateIndex(name: "IX_VaccineCatalogItems_TenantId_Code", schema: "public", table: "VaccineCatalogItems", columns: new[] { "TenantId", "Code" }, unique: true);
        VersionTrigger.Apply(migrationBuilder, "VaccineCatalogItems");
        migrationBuilder.CreateTable(name: "VisitHandoffLines", schema: "public", columns: t => new
        {
                Id = t.Column<string>(type: "character varying(26)", nullable: false, maxLength: 26),
                TenantId = t.Column<string>(type: "character varying(26)", nullable: false, maxLength: 26),
                UpdatedAt = t.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, precision: 6),
                Version = t.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                Deleted = t.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                VisitId = t.Column<string>(type: "text", nullable: false),
                OwnerId = t.Column<string>(type: "text", nullable: false),
                PetId = t.Column<string>(type: "text", nullable: false),
                DoctorId = t.Column<string>(type: "text", nullable: false),
                CompletedUtc = t.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                LinesJson = t.Column<string>(type: "jsonb", nullable: false),
                Status = t.Column<int>(type: "integer", nullable: false),
                InvoiceId = t.Column<string>(type: "text", nullable: true)
        }, constraints: t => t.PrimaryKey("PK_VisitHandoffLines", x => x.Id));
        migrationBuilder.CreateIndex(name: "IX_VisitHandoffLines_TenantId_VisitId", schema: "public", table: "VisitHandoffLines", columns: new[] { "TenantId", "VisitId" }, unique: true);
        migrationBuilder.CreateIndex(name: "IX_VisitHandoffLines_TenantId_Status_CompletedUtc", schema: "public", table: "VisitHandoffLines", columns: new[] { "TenantId", "Status", "CompletedUtc" }, unique: false);
        VersionTrigger.Apply(migrationBuilder, "VisitHandoffLines");
        migrationBuilder.CreateTable(name: "VisitRxLines", schema: "public", columns: t => new
        {
            OwnerId = t.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
            LineId = t.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
            ProductId = t.Column<string>(type: "text", nullable: true),
            DrugName = t.Column<string>(type: "text", nullable: false),
            Dosage = t.Column<string>(type: "text", nullable: false),
            Frequency = t.Column<string>(type: "text", nullable: false),
            DurationDays = t.Column<int>(type: "integer", nullable: false),
            Instructions = t.Column<string>(type: "text", nullable: true)
        }, constraints: t =>
        {
            t.PrimaryKey("PK_VisitRxLines", x => new { x.OwnerId, x.LineId });
            t.ForeignKey("FK_VisitRxLines_Visits_OwnerId", x => x.OwnerId, "Visits", "Id", principalSchema: "public", onDelete: ReferentialAction.Cascade);
        });
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "VisitRxLines", schema: "public");
        VersionTrigger.Drop(migrationBuilder, "VisitHandoffLines");
        migrationBuilder.DropTable(name: "VisitHandoffLines", schema: "public");
        VersionTrigger.Drop(migrationBuilder, "VaccineCatalogItems");
        migrationBuilder.DropTable(name: "VaccineCatalogItems", schema: "public");
        VersionTrigger.Drop(migrationBuilder, "TreatmentOccurrences");
        migrationBuilder.DropTable(name: "TreatmentOccurrences", schema: "public");
        VersionTrigger.Drop(migrationBuilder, "TreatmentPlans");
        migrationBuilder.DropTable(name: "TreatmentPlans", schema: "public");
        VersionTrigger.Drop(migrationBuilder, "VaccinationRecords");
        migrationBuilder.DropTable(name: "VaccinationRecords", schema: "public");
        VersionTrigger.Drop(migrationBuilder, "Visits");
        migrationBuilder.DropTable(name: "Visits", schema: "public");
        VersionTrigger.Drop(migrationBuilder, "ReceptionQueueEntries");
        migrationBuilder.DropTable(name: "ReceptionQueueEntries", schema: "public");
        VersionTrigger.Drop(migrationBuilder, "MedicalHistory");
        migrationBuilder.DropTable(name: "MedicalHistory", schema: "public");
        VersionTrigger.Drop(migrationBuilder, "DocumentReferences");
        migrationBuilder.DropTable(name: "DocumentReferences", schema: "public");
        VersionTrigger.Drop(migrationBuilder, "WeightEntries");
        migrationBuilder.DropTable(name: "WeightEntries", schema: "public");
        VersionTrigger.Drop(migrationBuilder, "Pets");
        migrationBuilder.DropTable(name: "Pets", schema: "public");
        VersionTrigger.Drop(migrationBuilder, "Owners");
        migrationBuilder.DropTable(name: "Owners", schema: "public");
    }
}
