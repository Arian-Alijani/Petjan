using System;
using Animora.Persistence.PostgreSql.Concurrency;
using Microsoft.EntityFrameworkCore.Migrations;
#nullable disable
namespace Animora.Persistence.PostgreSql.Migrations.Server;

public partial class _106_SchedulingBoarding : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(name: "Appointments", schema: "public", columns: t => new
        {
            Id = t.Column<string>(type: "character varying(26)", nullable: false, maxLength: 26),
            TenantId = t.Column<string>(type: "character varying(26)", nullable: false, maxLength: 26),
            UpdatedAt = t.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, precision: 6),
            Version = t.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
            Deleted = t.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
            PetId = t.Column<string>(type: "text", nullable: false),
            OwnerId = t.Column<string>(type: "text", nullable: false),
            DoctorUserId = t.Column<string>(type: "text", nullable: true),
            AppointmentTypeCode = t.Column<string>(type: "text", nullable: false),
            StartUtc = t.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            DurationMinutes = t.Column<int>(type: "integer", nullable: false),
            State = t.Column<int>(type: "integer", nullable: false),
            Source = t.Column<int>(type: "integer", nullable: false),
            Note = t.Column<string>(type: "text", nullable: true),
            VisitId = t.Column<string>(type: "text", nullable: true),
            CancelReason = t.Column<string>(type: "text", nullable: true),
            CheckedInUtc = t.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
            CreatedByUserId = t.Column<string>(type: "text", nullable: false),
            RescheduleCount = t.Column<int>(type: "integer", nullable: false)
        }, constraints: t => t.PrimaryKey("PK_Appointments", x => x.Id));
        migrationBuilder.CreateIndex(name: "IX_Appointments_TenantId_DoctorId_StartUtc", schema: "public", table: "Appointments", columns: new[] { "TenantId", "DoctorUserId", "StartUtc" }, unique: false);
        migrationBuilder.CreateIndex(name: "IX_Appointments_TenantId_StartUtc_DoctorUserId", schema: "public", table: "Appointments", columns: new[] { "TenantId", "StartUtc", "DoctorUserId" }, unique: false);
        migrationBuilder.CreateIndex(name: "IX_Appointments_TenantId_State_StartUtc", schema: "public", table: "Appointments", columns: new[] { "TenantId", "State", "StartUtc" }, unique: false);
        migrationBuilder.CreateIndex(name: "IX_Appointments_TenantId_PetId", schema: "public", table: "Appointments", columns: new[] { "TenantId", "PetId" }, unique: false);
        VersionTrigger.Apply(migrationBuilder, "Appointments");
        migrationBuilder.CreateTable(name: "Rooms", schema: "public", columns: t => new
        {
            Id = t.Column<string>(type: "character varying(26)", nullable: false, maxLength: 26),
            TenantId = t.Column<string>(type: "character varying(26)", nullable: false, maxLength: 26),
            UpdatedAt = t.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, precision: 6),
            Version = t.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
            Deleted = t.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
            Code = t.Column<string>(type: "text", nullable: false),
            DisplayName = t.Column<string>(type: "text", nullable: false),
            Type = t.Column<int>(type: "integer", nullable: false),
            Capacity = t.Column<int>(type: "integer", nullable: false),
            Status = t.Column<int>(type: "integer", nullable: false),
            Notes = t.Column<string>(type: "text", nullable: true)
        }, constraints: t => t.PrimaryKey("PK_Rooms", x => x.Id));
        migrationBuilder.CreateIndex(name: "IX_Rooms_TenantId_Code", schema: "public", table: "Rooms", columns: new[] { "TenantId", "Code" }, unique: true);
        VersionTrigger.Apply(migrationBuilder, "Rooms");
        migrationBuilder.CreateTable(name: "BoardingServiceDefs", schema: "public", columns: t => new
        {
            Id = t.Column<string>(type: "character varying(26)", nullable: false, maxLength: 26),
            TenantId = t.Column<string>(type: "character varying(26)", nullable: false, maxLength: 26),
            UpdatedAt = t.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, precision: 6),
            Version = t.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
            Deleted = t.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
            Code = t.Column<string>(type: "text", nullable: false),
            Title = t.Column<string>(type: "text", nullable: false),
            Billing = t.Column<int>(type: "integer", nullable: false),
            IsActive = t.Column<bool>(type: "boolean", nullable: false),
            UnitPriceMinor = t.Column<long>(type: "bigint", nullable: false),
            UnitPriceCurrency = t.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false)
        }, constraints: t => t.PrimaryKey("PK_BoardingServiceDefs", x => x.Id));
        migrationBuilder.CreateIndex(name: "IX_BoardingServiceDefs_TenantId_Code", schema: "public", table: "BoardingServiceDefs", columns: new[] { "TenantId", "Code" }, unique: true);
        VersionTrigger.Apply(migrationBuilder, "BoardingServiceDefs");
        migrationBuilder.CreateTable(name: "BoardingStays", schema: "public", columns: t => new
        {
            Id = t.Column<string>(type: "character varying(26)", nullable: false, maxLength: 26),
            TenantId = t.Column<string>(type: "character varying(26)", nullable: false, maxLength: 26),
            UpdatedAt = t.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, precision: 6),
            Version = t.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
            Deleted = t.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
            PetId = t.Column<string>(type: "text", nullable: false),
            OwnerId = t.Column<string>(type: "text", nullable: false),
            RoomId = t.Column<string>(type: "text", nullable: false),
            CheckInUtc = t.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            PlannedCheckOutUtc = t.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            ActualCheckOutUtc = t.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
            State = t.Column<int>(type: "integer", nullable: false),
            PaymentMode = t.Column<int>(type: "integer", nullable: false),
            CareNote = t.Column<string>(type: "text", nullable: true),
            DietNote = t.Column<string>(type: "text", nullable: true),
            InvoiceId = t.Column<string>(type: "text", nullable: true),
            PrepaidInvoiceId = t.Column<string>(type: "text", nullable: true),
            PrepaidRoomChargeMinor = t.Column<long>(type: "bigint", nullable: true),
            FrozenTotalMinor = t.Column<long>(type: "bigint", nullable: true),
            RoomHistory = t.Column<string>(type: "jsonb", nullable: false),
            DailyRateMinor = t.Column<long>(type: "bigint", nullable: false),
            DailyRateCurrency = t.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false)
        }, constraints: t => t.PrimaryKey("PK_BoardingStays", x => x.Id));
        migrationBuilder.CreateIndex(name: "IX_BoardingStays_TenantId_State_CheckInUtc", schema: "public", table: "BoardingStays", columns: new[] { "TenantId", "State", "CheckInUtc" }, unique: false);
        migrationBuilder.CreateIndex(name: "IX_BoardingStays_TenantId_RoomId_State", schema: "public", table: "BoardingStays", columns: new[] { "TenantId", "RoomId", "State" }, unique: false);
        VersionTrigger.Apply(migrationBuilder, "BoardingStays");
        migrationBuilder.CreateTable(name: "AppointmentSettings", schema: "public", columns: t => new
        {
            Id = t.Column<string>(type: "character varying(26)", nullable: false, maxLength: 26),
            TenantId = t.Column<string>(type: "character varying(26)", nullable: false, maxLength: 26),
            UpdatedAt = t.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, precision: 6),
            Version = t.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
            Deleted = t.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
            SettingsJson = t.Column<string>(type: "jsonb", nullable: false)
        }, constraints: t => t.PrimaryKey("PK_AppointmentSettings", x => x.Id));
        migrationBuilder.CreateIndex(name: "IX_AppointmentSettings_TenantId", schema: "public", table: "AppointmentSettings", columns: new[] { "TenantId" }, unique: true);
        VersionTrigger.Apply(migrationBuilder, "AppointmentSettings");
        migrationBuilder.CreateTable(name: "BoardingHandoffLines", schema: "public", columns: t => new
        {
            Id = t.Column<string>(type: "character varying(26)", nullable: false, maxLength: 26),
            TenantId = t.Column<string>(type: "character varying(26)", nullable: false, maxLength: 26),
            UpdatedAt = t.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, precision: 6),
            Version = t.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
            Deleted = t.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
            StayId = t.Column<string>(type: "text", nullable: false),
            OwnerId = t.Column<string>(type: "text", nullable: false),
            PetId = t.Column<string>(type: "text", nullable: false),
            DoctorId = t.Column<string>(type: "text", nullable: false),
            CompletedUtc = t.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            LinesJson = t.Column<string>(type: "jsonb", nullable: false),
            Status = t.Column<int>(type: "integer", nullable: false),
            Kind = t.Column<int>(type: "integer", nullable: false),
            InvoiceId = t.Column<string>(type: "text", nullable: true)
        }, constraints: t => t.PrimaryKey("PK_BoardingHandoffLines", x => x.Id));
        migrationBuilder.CreateIndex(name: "IX_BoardingHandoffLines_TenantId_StayId", schema: "public", table: "BoardingHandoffLines", columns: new[] { "TenantId", "StayId" }, unique: false);
        migrationBuilder.CreateIndex(name: "IX_BoardingHandoffLines_TenantId_Status_CompletedUtc", schema: "public", table: "BoardingHandoffLines", columns: new[] { "TenantId", "Status", "CompletedUtc" }, unique: false);
        VersionTrigger.Apply(migrationBuilder, "BoardingHandoffLines");
        migrationBuilder.CreateTable(name: "BoardingStayItems", schema: "public", columns: t => new
        {
            OwnerId = t.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
            ItemId = t.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
            Kind = t.Column<int>(type: "integer", nullable: false),
            CatalogItemId = t.Column<string>(type: "text", nullable: true),
            Title = t.Column<string>(type: "text", nullable: false),
            QuantityScaled = t.Column<long>(type: "bigint", nullable: false),
            QuantityUnit = t.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
            UnitPriceMinor = t.Column<long>(type: "bigint", nullable: false),
            UnitPriceCurrency = t.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
            ConsumedUtc = t.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            RecordedByUserId = t.Column<string>(type: "text", nullable: false)
        }, constraints: t =>
        {
            t.PrimaryKey("PK_BoardingStayItems", x => new { x.OwnerId, x.ItemId });
            t.ForeignKey("FK_BoardingStayItems_BoardingStays_OwnerId", x => x.OwnerId, "BoardingStays", "Id", principalSchema: "public", onDelete: ReferentialAction.Cascade);
        });
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "BoardingStayItems", schema: "public");
        VersionTrigger.Drop(migrationBuilder, "BoardingHandoffLines");
        migrationBuilder.DropTable(name: "BoardingHandoffLines", schema: "public");
        VersionTrigger.Drop(migrationBuilder, "AppointmentSettings");
        migrationBuilder.DropTable(name: "AppointmentSettings", schema: "public");
        VersionTrigger.Drop(migrationBuilder, "BoardingStays");
        migrationBuilder.DropTable(name: "BoardingStays", schema: "public");
        VersionTrigger.Drop(migrationBuilder, "BoardingServiceDefs");
        migrationBuilder.DropTable(name: "BoardingServiceDefs", schema: "public");
        VersionTrigger.Drop(migrationBuilder, "Rooms");
        migrationBuilder.DropTable(name: "Rooms", schema: "public");
        VersionTrigger.Drop(migrationBuilder, "Appointments");
        migrationBuilder.DropTable(name: "Appointments", schema: "public");
    }
}
