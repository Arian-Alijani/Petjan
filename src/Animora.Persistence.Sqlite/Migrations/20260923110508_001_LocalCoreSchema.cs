using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Animora.Persistence.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class _001_LocalCoreSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Appointments",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    PetId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    DoctorUserId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: true),
                    AppointmentTypeCode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    StartUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    DurationMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    Source = table.Column<int>(type: "INTEGER", nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    VisitId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: true),
                    CancelReason = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    CheckedInUtc = table.Column<long>(type: "INTEGER", nullable: true),
                    CreatedByUserId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    RescheduleCount = table.Column<int>(type: "INTEGER", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    Version = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    Deleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Appointments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BoardingServiceDefs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Billing = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    UnitPriceCurrency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    UnitPriceMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    Version = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    Deleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BoardingServiceDefs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BoardingStays",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    PetId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    RoomId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    CheckInUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    PlannedCheckOutUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    ActualCheckOutUtc = table.Column<long>(type: "INTEGER", nullable: true),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    CareNote = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    DietNote = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    InvoiceId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: true),
                    Items = table.Column<string>(type: "TEXT", nullable: false),
                    RoomHistory = table.Column<string>(type: "TEXT", nullable: false),
                    DailyRateCurrency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    DailyRateMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    Version = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    Deleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BoardingStays", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Devices",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    DeviceName = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    InstallationIdHash = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    Platform = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    RegisteredUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    LastContactUtc = table.Column<long>(type: "INTEGER", nullable: true),
                    RevokedUtc = table.Column<long>(type: "INTEGER", nullable: true),
                    RevocationReason = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    Version = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    Deleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Devices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DocumentReferences",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: true),
                    PetId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: true),
                    VisitId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    FileId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    ContentHashSha256 = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    UploadedUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    UploadedByUserId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    Version = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    Deleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentReferences", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MedicalHistoryEntries",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    PetId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Detail = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    OccurredUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    SourceEntityId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: true),
                    SourceEntityType = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    RecordedByUserId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    Version = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    Deleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MedicalHistoryEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Owners",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    FullName = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    PhonePrimary = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    PhoneSecondary = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    NationalId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: true),
                    Email = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    Address = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    City = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    Gender = table.Column<int>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    RegisteredUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    IsBlocked = table.Column<bool>(type: "INTEGER", nullable: false),
                    BlockReason = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    Version = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    Deleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Owners", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Permissions",
                columns: table => new
                {
                    Code = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    Risk = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Permissions", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "Pets",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Species = table.Column<int>(type: "INTEGER", nullable: false),
                    Breed = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    Gender = table.Column<int>(type: "INTEGER", nullable: false),
                    BirthDateUtc = table.Column<long>(type: "INTEGER", nullable: true),
                    IsNeutered = table.Column<bool>(type: "INTEGER", nullable: false),
                    Color = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    MicrochipCode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    MicrochipImplantUtc = table.Column<long>(type: "INTEGER", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    AllergyNotes = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    ChronicNotes = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    RegisteredUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    Version = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    Deleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Pets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReceptionQueueEntries",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    PetId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    AppointmentId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: true),
                    DoctorUserId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: true),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    Source = table.Column<int>(type: "INTEGER", nullable: false),
                    TicketNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    EnqueuedUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    CalledUtc = table.Column<long>(type: "INTEGER", nullable: true),
                    ClosedUtc = table.Column<long>(type: "INTEGER", nullable: true),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    VisitId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: true),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    Version = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    Deleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReceptionQueueEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RoleDefinitions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    Role = table.Column<int>(type: "INTEGER", nullable: false),
                    DisplayKey = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    IsSystem = table.Column<bool>(type: "INTEGER", nullable: false),
                    PermissionCodes = table.Column<string>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    Version = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    Deleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoleDefinitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Rooms",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    Capacity = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    Version = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    Deleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Rooms", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StaffMemberships",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    Role = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    JoinedUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    EndedUtc = table.Column<long>(type: "INTEGER", nullable: true),
                    DeniedPermissionCodes = table.Column<string>(type: "TEXT", nullable: false),
                    ExtraPermissionCodes = table.Column<string>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    Version = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    Deleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffMemberships", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Tenants",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    LegalName = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    Timezone = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    DefaultCurrency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    Version = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    Deleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tenants", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    UserName = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    SecurityStamp = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    LastLoginUtc = table.Column<long>(type: "INTEGER", nullable: true),
                    FailedAttemptCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LockoutEndUtc = table.Column<long>(type: "INTEGER", nullable: true),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    Version = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    Deleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VaccinationRecords",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    PetId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    VaccineCode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    VaccineName = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    DoseNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalDoses = table.Column<int>(type: "INTEGER", nullable: true),
                    AdministeredUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    AdministeredByUserId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    BatchNumber = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    NextDueUtc = table.Column<long>(type: "INTEGER", nullable: true),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    VisitId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    Version = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    Deleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VaccinationRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Visits",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    PetId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    AppointmentId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: true),
                    DoctorUserId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    Reason = table.Column<int>(type: "INTEGER", nullable: false),
                    ChiefComplaint = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    StartedUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    CompletedUtc = table.Column<long>(type: "INTEGER", nullable: true),
                    Vitals_WeightKg = table.Column<string>(type: "TEXT", nullable: true),
                    Vitals_TemperatureC = table.Column<string>(type: "TEXT", nullable: true),
                    Vitals_HeartRateBpm = table.Column<int>(type: "INTEGER", nullable: true),
                    Vitals_RespiratoryRate = table.Column<int>(type: "INTEGER", nullable: true),
                    Vitals_MucousMembrane = table.Column<string>(type: "TEXT", nullable: true),
                    Vitals_Hydration = table.Column<string>(type: "TEXT", nullable: true),
                    Diagnosis = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    TreatmentPlan = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    DoctorNotes = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    InvoiceId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: true),
                    CancellationReason = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    Prescriptions = table.Column<string>(type: "TEXT", nullable: false),
                    ServiceLines = table.Column<string>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    Version = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    Deleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Visits", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WeightEntries",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    PetId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    WeightKg = table.Column<string>(type: "TEXT", nullable: false),
                    MeasuredUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    RecordedByUserId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    CorrectsEntryId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: true),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 26, nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    Version = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    Deleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WeightEntries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_TenantId_PetId",
                table: "Appointments",
                columns: new[] { "TenantId", "PetId" });

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_TenantId_StartUtc_DoctorUserId",
                table: "Appointments",
                columns: new[] { "TenantId", "StartUtc", "DoctorUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_TenantId_State_StartUtc",
                table: "Appointments",
                columns: new[] { "TenantId", "State", "StartUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_TenantId_UpdatedAt",
                table: "Appointments",
                columns: new[] { "TenantId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BoardingServiceDefs_TenantId_UpdatedAt",
                table: "BoardingServiceDefs",
                columns: new[] { "TenantId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BoardingStays_TenantId_State_CheckInUtc",
                table: "BoardingStays",
                columns: new[] { "TenantId", "State", "CheckInUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_BoardingStays_TenantId_UpdatedAt",
                table: "BoardingStays",
                columns: new[] { "TenantId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Devices_TenantId_InstallationIdHash",
                table: "Devices",
                columns: new[] { "TenantId", "InstallationIdHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Devices_TenantId_UpdatedAt",
                table: "Devices",
                columns: new[] { "TenantId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentReferences_TenantId_UpdatedAt",
                table: "DocumentReferences",
                columns: new[] { "TenantId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MedicalHistoryEntries_TenantId_PetId_OccurredUtc",
                table: "MedicalHistoryEntries",
                columns: new[] { "TenantId", "PetId", "OccurredUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MedicalHistoryEntries_TenantId_UpdatedAt",
                table: "MedicalHistoryEntries",
                columns: new[] { "TenantId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Owners_TenantId_FullName",
                table: "Owners",
                columns: new[] { "TenantId", "FullName" });

            migrationBuilder.CreateIndex(
                name: "IX_Owners_TenantId_Phone",
                table: "Owners",
                columns: new[] { "TenantId", "PhonePrimary" });

            migrationBuilder.CreateIndex(
                name: "IX_Owners_TenantId_UpdatedAt",
                table: "Owners",
                columns: new[] { "TenantId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Pets_TenantId_Name",
                table: "Pets",
                columns: new[] { "TenantId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_Pets_TenantId_OwnerId",
                table: "Pets",
                columns: new[] { "TenantId", "OwnerId" });

            migrationBuilder.CreateIndex(
                name: "IX_Pets_TenantId_UpdatedAt",
                table: "Pets",
                columns: new[] { "TenantId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ReceptionQueueEntries_TenantId_State_EnqueuedUtc",
                table: "ReceptionQueueEntries",
                columns: new[] { "TenantId", "State", "EnqueuedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ReceptionQueueEntries_TenantId_UpdatedAt",
                table: "ReceptionQueueEntries",
                columns: new[] { "TenantId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RoleDefinitions_TenantId_UpdatedAt",
                table: "RoleDefinitions",
                columns: new[] { "TenantId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Rooms_TenantId_Code",
                table: "Rooms",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Rooms_TenantId_UpdatedAt",
                table: "Rooms",
                columns: new[] { "TenantId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_StaffMemberships_TenantId_UpdatedAt",
                table: "StaffMemberships",
                columns: new[] { "TenantId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_StaffMemberships_TenantId_UserId_Role",
                table: "StaffMemberships",
                columns: new[] { "TenantId", "UserId", "Role" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_TenantId_UpdatedAt",
                table: "Tenants",
                columns: new[] { "TenantId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Users_TenantId_UpdatedAt",
                table: "Users",
                columns: new[] { "TenantId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Users_TenantId_Username",
                table: "Users",
                columns: new[] { "TenantId", "UserName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VaccinationRecords_TenantId_DueUtc_State",
                table: "VaccinationRecords",
                columns: new[] { "TenantId", "NextDueUtc", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_VaccinationRecords_TenantId_UpdatedAt",
                table: "VaccinationRecords",
                columns: new[] { "TenantId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Visits_TenantId_PetId_StartedUtc",
                table: "Visits",
                columns: new[] { "TenantId", "PetId", "StartedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Visits_TenantId_State_StartedUtc",
                table: "Visits",
                columns: new[] { "TenantId", "State", "StartedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Visits_TenantId_UpdatedAt",
                table: "Visits",
                columns: new[] { "TenantId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WeightEntries_TenantId_UpdatedAt",
                table: "WeightEntries",
                columns: new[] { "TenantId", "UpdatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Appointments");

            migrationBuilder.DropTable(
                name: "BoardingServiceDefs");

            migrationBuilder.DropTable(
                name: "BoardingStays");

            migrationBuilder.DropTable(
                name: "Devices");

            migrationBuilder.DropTable(
                name: "DocumentReferences");

            migrationBuilder.DropTable(
                name: "MedicalHistoryEntries");

            migrationBuilder.DropTable(
                name: "Owners");

            migrationBuilder.DropTable(
                name: "Permissions");

            migrationBuilder.DropTable(
                name: "Pets");

            migrationBuilder.DropTable(
                name: "ReceptionQueueEntries");

            migrationBuilder.DropTable(
                name: "RoleDefinitions");

            migrationBuilder.DropTable(
                name: "Rooms");

            migrationBuilder.DropTable(
                name: "StaffMemberships");

            migrationBuilder.DropTable(
                name: "Tenants");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "VaccinationRecords");

            migrationBuilder.DropTable(
                name: "Visits");

            migrationBuilder.DropTable(
                name: "WeightEntries");
        }
    }
}
