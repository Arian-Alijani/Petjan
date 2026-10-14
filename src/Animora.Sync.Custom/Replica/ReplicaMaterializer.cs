using System.Globalization;
using System.Text;
using System.Text.Json;
using Animora.Application.Activity;
using Animora.Contracts.Diagnostics;
using Animora.Domain.Events;
using Animora.Domain.Primitives;
using Animora.Persistence.Sqlite.Activity;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Entities;
using Animora.Persistence.Sqlite.Replica;
using Animora.Persistence.Sqlite.Search;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Animora.Sync.Custom.Replica;

/// <summary>
/// Projects a freshly staged batch from the generic replica (<c>repl_*</c>) onto the typed business tables the screens read, so a record
/// authored by another user of the clinic becomes visible locally. Surface: reception (Owner, Pet, Appointment, ReceptionQueueEntry),
/// inventory/purchasing/sales/finance catalogues (UnitOfMeasure, ProductCategory, Product + its variants, Supplier, Insurer, LedgerAccount,
/// AccountingPeriod), clinical (WeightEntry, MedicalHistoryEntry, VaccinationRecord, VaccineCatalogItem, Visit, TreatmentPlan,
/// TreatmentOccurrence), boarding (Room, BoardingServiceDef, BoardingStay) and identity (RoleDefinition, StaffMembership).
/// <para>Deliberately NOT projected here: <c>Tenant</c> (the local installation row owns the tenant binding), <c>DocumentReference</c>
/// (file payloads are not replicated) and the per-visit / per-stay <i>billing hand-off</i> child rows (<c>BillingHandoffs</c>; a derived,
/// device-local cashier queue that is rebuilt from the visit/stay itself).</para>
/// <para>
/// Runs inside the apply store's commit transaction, so base + checkpoint + business rows move together or not at all. Rules:
/// <list type="bullet">
/// <item>Server bookkeeping (<c>UpdatedAt</c>, <c>Version</c>, <c>Deleted</c>) is copied verbatim — raw SQL on purpose, so the local
/// <c>SaveChanges</c> stamping never rewrites the server's values.</item>
/// <item>A record whose aggregate has an unsettled local command (<c>LocallyPosted</c>/<c>PendingSettlement</c>) is <b>not</b> written: the local
/// intent wins until the server settles it (the pending overlay is never edited by a pull — G04 non-destructive rule). The row still reaches
/// <c>repl_base</c>.</item>
/// <item>A wire tombstone (<c>Op = 'D'</c>) soft-deletes the local row; rows absent from a full snapshot are <b>never</b> removed (a snapshot
/// must not destroy local-first data the server has not seen yet).</item>
/// <item>A record with a missing required field is skipped rather than failing the batch (one poisoned row must not stall sync forever).</item>
/// <item><b>Actor provenance (TASK-8):</b> a freshly synced <c>Appointment</c> also appends its «booked» History row with the actor taken
/// from the record's own <c>CreatedByUserId</c> — the author on the originating device, not the local session user. Entities whose wire
/// contract carries no author (Pet, Owner, queue entry) get no fabricated actor row.</item>
/// </list>
/// </para>
/// </summary>
internal static class ReplicaMaterializer
{
    /// <summary>
    /// Column value kinds. <c>IntOrNull</c>/<c>Time</c> keep an absent value as SQL NULL; <c>Json</c> stores a JSON document verbatim
    /// (<c>[]</c> when absent); <c>PascalJson</c> stores a wire JSON document re-keyed to the local PascalCase JSON-column shape;
    /// <c>StayItems</c> rebuilds the local <c>StayItem</c> JSON list from the server's flat child-row images; <c>EmptySet</c> always writes <c>[]</c>
    /// (a NOT NULL local JSON set the wire does not carry).
    /// </summary>
    private enum Kind { Text, Int, IntOrNull, Bool, Time, Json, PascalJson, StayItems, EmptySet }

    /// <param name="Name">Local SQLite column name.</param>
    /// <param name="Kind">How the wire value is converted.</param>
    /// <param name="Required">A missing/empty value skips the whole row (it cannot be stored).</param>
    /// <param name="Path">Dotted camelCase wire path; defaults to the camelCased <paramref name="Name"/>. Used for value objects
    /// (<c>dailyRate.minor</c>) and nested objects (<c>vitals.weightKg</c>).</param>
    /// <param name="Default">Text used when the wire value is absent (NOT NULL columns with a sane fallback).</param>
    /// <param name="FallbackColumn">Another (earlier) column whose value is used when this one is absent.</param>
    private sealed record Col(string Name, Kind Kind, bool Required = false, string? Path = null, string? Default = null, string? FallbackColumn = null)
    {
        public string Json { get; } = Path ?? JsonNamingPolicy.CamelCase.ConvertName(Name);
    }

    /// <summary>A flat child table of a root (e.g. <c>ProductVariants</c>), rewritten wholesale from the wire array on every root upsert.</summary>
    private sealed class ChildMap(string json, string table, string ownerColumn, Col[] columns)
    {
        public string Json { get; } = json;
        public string Table { get; } = table;
        public Col[] Columns { get; } = columns;
        public string DeleteSql { get; } = $"DELETE FROM \"{table}\" WHERE \"{ownerColumn}\" = {{0}}";
        public string InsertSql { get; } = BuildChildInsert(table, ownerColumn, columns);
    }

    private sealed class EntityMap(string entity, string table, Col[] columns, Func<IReadOnlyDictionary<string, object?>, string>? searchKey = null, ChildMap[]? children = null)
    {
        public ChildMap[] Children { get; } = children ?? [];
        public string Entity { get; } = entity;
        public string Table { get; } = table;
        public Col[] Columns { get; } = columns;
        public Func<IReadOnlyDictionary<string, object?>, string>? SearchKey { get; } = searchKey;

        public string UpsertSql { get; } = BuildUpsert(table, columns, searchKey is not null);
        public string TombstoneSql { get; } =
            $"UPDATE \"{table}\" SET \"Deleted\" = 1, \"UpdatedAt\" = {{0}}, \"Version\" = {{1}} WHERE \"Id\" = {{2}} AND \"TenantId\" = {{3}}";
    }

    /// <summary>Currency assumed for a money value object the wire omitted (the product's single supported currency).</summary>
    private const string DefaultCurrency = "IRT";

    /// <summary>SQLite primary result code <c>SQLITE_CONSTRAINT</c>.</summary>
    private const int SqliteConstraintErrorCode = 19;

    private static string? Str(IReadOnlyDictionary<string, object?> v, string name) => v[name] as string;

    private static readonly EntityMap[] Maps =
    [
        new("Owner", "Owners",
        [
            new("FullName", Kind.Text, true), new("PhonePrimary", Kind.Text, true), new("PhoneSecondary", Kind.Text), new("NationalId", Kind.Text),
            new("Email", Kind.Text), new("Address", Kind.Text), new("City", Kind.Text), new("Gender", Kind.Int), new("Notes", Kind.Text),
            new("RegisteredUtc", Kind.Time, true), new("IsBlocked", Kind.Bool), new("BlockReason", Kind.Text),
        ], v => OwnerSearchKeyProvider.Compose(Str(v, "FullName")!, Str(v, "PhonePrimary")!, Str(v, "PhoneSecondary"), Str(v, "NationalId"))),
        new("Pet", "Pets",
        [
            new("OwnerId", Kind.Text, true), new("Name", Kind.Text, true), new("Species", Kind.Int), new("Breed", Kind.Text), new("Gender", Kind.Int),
            new("BirthDateUtc", Kind.Time), new("IsNeutered", Kind.Bool), new("Color", Kind.Text), new("MicrochipCode", Kind.Text),
            new("MicrochipImplantUtc", Kind.Time), new("Status", Kind.Int), new("AllergyNotes", Kind.Text), new("ChronicNotes", Kind.Text),
            new("RegisteredUtc", Kind.Time, true),
        ], v => PetSearchKeyProvider.Compose(Str(v, "Name")!, Str(v, "Breed"), Str(v, "MicrochipCode"))),
        new("Appointment", "Appointments",
        [
            new("PetId", Kind.Text, true), new("OwnerId", Kind.Text, true), new("DoctorUserId", Kind.Text), new("AppointmentTypeCode", Kind.Text, true),
            new("StartUtc", Kind.Time, true), new("DurationMinutes", Kind.Int), new("State", Kind.Int), new("Source", Kind.Int), new("Note", Kind.Text),
            new("VisitId", Kind.Text), new("CancelReason", Kind.Text), new("CheckedInUtc", Kind.Time), new("CreatedByUserId", Kind.Text, true),
            new("RescheduleCount", Kind.Int),
        ]),
        new("ReceptionQueueEntry", "ReceptionQueueEntries",
        [
            new("PetId", Kind.Text, true), new("OwnerId", Kind.Text, true), new("AppointmentId", Kind.Text), new("DoctorUserId", Kind.Text),
            new("State", Kind.Int), new("Source", Kind.Int), new("TicketNumber", Kind.Int), new("EnqueuedUtc", Kind.Time, true),
            new("CalledUtc", Kind.Time), new("ClosedUtc", Kind.Time), new("Priority", Kind.Int), new("Note", Kind.Text), new("VisitId", Kind.Text),
        ]),

        // ── Inventory / purchasing / sales / finance catalogues ──
        new("UnitOfMeasure", "UnitOfMeasures",
        [
            new("Code", Kind.Text, true), new("DisplayKey", Kind.Text, true), new("DecimalPlaces", Kind.Int), new("BaseUnitCode", Kind.Text),
            new("ConversionToBase", Kind.Text),
        ]),
        new("ProductCategory", "ProductCategories",
        [
            new("Code", Kind.Text, true), new("DisplayName", Kind.Text, true), new("ParentCategoryId", Kind.Text), new("SortOrder", Kind.Int),
            new("IsActive", Kind.Bool),
        ]),
        new("Product", "Products",
        [
            new("Sku", Kind.Text, true), new("Name", Kind.Text, true), new("Brand", Kind.Text), new("CategoryId", Kind.Text, true), new("Kind", Kind.Int),
            new("UnitCode", Kind.Text, true), new("IsPrescriptionOnly", Kind.Bool), new("IsActive", Kind.Bool), new("TrackStock", Kind.Bool),
            new("ReorderLevelValue", Kind.Text, Path: "reorderLevel.value", Default: "0"),
            new("ReorderLevelUnit", Kind.Text, Path: "reorderLevel.unitCode", FallbackColumn: "UnitCode"),
            new("DefaultCostPriceMinor", Kind.Int, Path: "defaultCostPrice.minor"),
            new("DefaultCostPriceCurrency", Kind.Text, Path: "defaultCostPrice.currency", Default: DefaultCurrency),
            new("DefaultSalePriceMinor", Kind.Int, Path: "defaultSalePrice.minor"),
            new("DefaultSalePriceCurrency", Kind.Text, Path: "defaultSalePrice.currency", Default: DefaultCurrency),
            new("ShelfLifeDays", Kind.IntOrNull), new("Notes", Kind.Text),
        ], children:
        [
            new("variants", "ProductVariants", "OwnerId",
            [
                new("VariantId", Kind.Text, true), new("Sku", Kind.Text, true), new("AttributeName", Kind.Text, true), new("AttributeValue", Kind.Text, true),
                new("CostPriceMinor", Kind.Int), new("CostPriceCurrency", Kind.Text, Default: DefaultCurrency),
                new("SalePriceMinor", Kind.Int), new("SalePriceCurrency", Kind.Text, Default: DefaultCurrency),
                new("ReorderLevelScaled", Kind.Int), new("ReorderLevelUnit", Kind.Text, Default: ""), new("Barcode", Kind.Text), new("IsActive", Kind.Bool),
            ]),
        ]),
        new("Supplier", "Suppliers",
        [
            new("Code", Kind.Text, true), new("Name", Kind.Text, true), new("ContactPerson", Kind.Text), new("Phone", Kind.Text), new("Email", Kind.Text),
            new("Address", Kind.Text), new("NationalId", Kind.Text), new("PaymentTermDays", Kind.Int), new("IsActive", Kind.Bool), new("Notes", Kind.Text),
        ]),
        new("Insurer", "Insurers",
        [
            new("Code", Kind.Text, true), new("Name", Kind.Text, true), new("CoveragePercent", Kind.Text, Default: "0"), new("CeilingMinor", Kind.Int),
        ]),
        new("LedgerAccount", "LedgerAccounts",
        [
            new("Code", Kind.Text, true), new("Name", Kind.Text, true), new("Type", Kind.Int), new("ParentAccountId", Kind.Text), new("IsPostable", Kind.Bool),
            new("IsActive", Kind.Bool), new("Description", Kind.Text),
        ]),
        new("AccountingPeriod", "AccountingPeriods",
        [
            new("PeriodCode", Kind.Text, true), new("StartUtc", Kind.Time, true), new("EndUtc", Kind.Time, true), new("State", Kind.Int), new("ClosedUtc", Kind.Time),
            new("ClosedByUserId", Kind.Text), new("ClosingEntryId", Kind.Text), new("Note", Kind.Text),
        ]),

        // ── Clinical ──
        new("WeightEntry", "WeightEntries",
        [
            new("PetId", Kind.Text, true), new("WeightKg", Kind.Text, true), new("MeasuredUtc", Kind.Time, true), new("Note", Kind.Text),
            new("RecordedByUserId", Kind.Text, true), new("CorrectsEntryId", Kind.Text),
        ]),
        new("MedicalHistoryEntry", "MedicalHistoryEntries",
        [
            new("PetId", Kind.Text, true), new("Kind", Kind.Int), new("Title", Kind.Text, true), new("Detail", Kind.Text), new("OccurredUtc", Kind.Time, true),
            new("SourceEntityId", Kind.Text), new("SourceEntityType", Kind.Text), new("RecordedByUserId", Kind.Text, true),
        ]),
        new("VaccinationRecord", "VaccinationRecords",
        [
            new("PetId", Kind.Text, true), new("VaccineCode", Kind.Text, true), new("VaccineName", Kind.Text, true), new("DoseNumber", Kind.Int),
            new("TotalDoses", Kind.IntOrNull), new("AdministeredUtc", Kind.Time, true), new("AdministeredByUserId", Kind.Text, true), new("BatchNumber", Kind.Text),
            new("NextDueUtc", Kind.Time), new("State", Kind.Int), new("VisitId", Kind.Text), new("Notes", Kind.Text),
        ]),
        new("VaccineCatalogItem", "VaccineCatalogItems",
        [
            new("Code", Kind.Text, true), new("Name", Kind.Text, true), new("Protects", Kind.Text), new("TotalDoses", Kind.Int), new("BoosterDays", Kind.Int),
            new("Stock", Kind.Int), new("MinStock", Kind.Int), new("IsActive", Kind.Bool),
        ]),
        new("Visit", "Visits",
        [
            new("PetId", Kind.Text, true), new("OwnerId", Kind.Text, true), new("AppointmentId", Kind.Text), new("DoctorUserId", Kind.Text, true), new("State", Kind.Int),
            new("Reason", Kind.Int), new("ChiefComplaint", Kind.Text), new("StartedUtc", Kind.Time, true), new("CompletedUtc", Kind.Time),
            new("Vitals_WeightKg", Kind.Text, Path: "vitals.weightKg"), new("Vitals_TemperatureC", Kind.Text, Path: "vitals.temperatureC"),
            new("Diagnosis", Kind.Text), new("TreatmentPlan", Kind.Text), new("DoctorNotes", Kind.Text), new("InvoiceId", Kind.Text), new("CancellationReason", Kind.Text),
            new("Prescriptions", Kind.PascalJson), new("ServiceLines", Kind.PascalJson),
        ]),
        new("TreatmentPlan", "TreatmentPlans",
        [
            new("PetId", Kind.Text, true), new("ItemType", Kind.Int), new("Kind", Kind.Int), new("Title", Kind.Text, true), new("VaccineCode", Kind.Text),
            new("Dosage", Kind.Text), new("Notes", Kind.Text), new("NotifyOwner", Kind.Bool), new("ScheduleType", Kind.Int),
            new("CreatedByUserId", Kind.Text, true), new("CreatedUtc", Kind.Time, true), new("State", Kind.Int),
        ]),
        new("TreatmentOccurrence", "TreatmentOccurrences",
        [
            new("PlanId", Kind.Text, true), new("DueDay", Kind.Text, true), new("SortOrder", Kind.Int), new("State", Kind.Int), new("DoneUtc", Kind.Time),
            new("DoneByUserId", Kind.Text),
        ]),

        // ── Boarding ──
        new("Room", "Rooms",
        [
            new("Code", Kind.Text, true), new("DisplayName", Kind.Text, true), new("Type", Kind.Int), new("Capacity", Kind.Int), new("Status", Kind.Int),
            new("Notes", Kind.Text),
        ]),
        new("BoardingServiceDef", "BoardingServiceDefs",
        [
            new("Code", Kind.Text, true), new("Title", Kind.Text, true),
            new("UnitPriceMinor", Kind.Int, Path: "unitPrice.minor"), new("UnitPriceCurrency", Kind.Text, Path: "unitPrice.currency", Default: DefaultCurrency),
            new("Billing", Kind.Int), new("IsActive", Kind.Bool),
        ]),
        new("BoardingStay", "BoardingStays",
        [
            new("PetId", Kind.Text, true), new("OwnerId", Kind.Text, true), new("RoomId", Kind.Text, true), new("CheckInUtc", Kind.Time, true),
            new("PlannedCheckOutUtc", Kind.Time, true), new("ActualCheckOutUtc", Kind.Time), new("State", Kind.Int),
            new("DailyRateMinor", Kind.Int, Path: "dailyRate.minor"), new("DailyRateCurrency", Kind.Text, Path: "dailyRate.currency", Default: DefaultCurrency),
            new("PaymentMode", Kind.Int), new("CareNote", Kind.Text), new("DietNote", Kind.Text), new("InvoiceId", Kind.Text), new("PrepaidInvoiceId", Kind.Text),
            new("PrepaidRoomChargeMinor", Kind.IntOrNull), new("FrozenTotalMinor", Kind.IntOrNull),
            new("Items", Kind.StayItems), new("RoomHistory", Kind.PascalJson),
        ]),

        // ── Identity (role catalogue + staff membership) ──
        new("RoleDefinition", "RoleDefinitions",
        [
            new("Role", Kind.Int), new("DisplayKey", Kind.Text, true), new("IsSystem", Kind.Bool), new("PermissionCodes", Kind.Json),
        ]),
        // The wire contract carries no per-user permission overrides, so the two NOT NULL local override sets are written as empty sets.
        new("StaffMembership", "StaffMemberships",
        [
            new("UserId", Kind.Text, true), new("Role", Kind.Int), new("Status", Kind.Int), new("JoinedUtc", Kind.Time, true), new("EndedUtc", Kind.Time),
            new("WorkStartMinute", Kind.Int), new("WorkEndMinute", Kind.Int), new("WorkDaysMask", Kind.Int),
            new("ExtraPermissionCodes", Kind.EmptySet), new("DeniedPermissionCodes", Kind.EmptySet),
        ]),
    ];

    private static readonly string[] Entities = Maps.Select(m => m.Entity).ToArray();

    /// <summary>The wire entity names this materializer projects (exposed for tests/diagnostics).</summary>
    internal static IReadOnlyList<string> SupportedEntities => Entities;

    /// <summary>Projects the staged rows of <paramref name="batchId"/>; returns the number of business rows written. Must run inside the caller's transaction.</summary>
    internal static async Task<int> ApplyAsync(
        AnimoraLocalDbContext db, string tenant, string batchId, TimeZoneInfo zone, CancellationToken ct)
    {
        var pending = await LoadPendingAsync(db, tenant, ct).ConfigureAwait(false);
        TraceLog.Info($"ReplicaMaterializer.Apply — tenant={tenant}, batch={batchId}, entities=[{string.Join(", ", Entities)}], unsettledPendingKeys={pending.Count}.", "sync.apply");
        var rows = await db.Set<ReplicaStaging>().AsNoTracking()
            .Where(x => x.TenantId == tenant && x.BatchId == batchId && Entities.Contains(x.Entity))
            .OrderBy(x => x.PageOrdinal)
            .ToListAsync(ct).ConfigureAwait(false); // materialised: the connection is reused for the writes below

        TraceLog.Info($"ReplicaMaterializer.Apply — {rows.Count} staged row(s) match the projected entity set.", "sync.apply");
        var written = 0;
        var booked = new List<ActivityEventDraft>();
        var byEntity = Maps.ToDictionary(m => m.Entity, StringComparer.Ordinal);

        foreach (var row in rows)
        {
            if (pending.Contains(Key(row.Entity, row.EntityId)))
            {
                TraceLog.Warn($"ReplicaMaterializer.Apply — SKIP {row.Entity}/{row.EntityId}: a local UNSETTLED command (LocallyPosted/PendingSettlement) overlays it, so the local intent wins and the server row is NOT written to the typed table (it is still in repl_base).", "sync.apply");
                continue;
            }
            var map = byEntity[row.Entity];
            var updated = (row.UpdatedAtUtc ?? DateTimeOffset.UtcNow).UtcTicks;

            if (row.Payload is null)
            {
                var tombstoned = await db.Database.ExecuteSqlRawAsync(map.TombstoneSql, new object[] { updated, Nullable(row.Version), row.EntityId, tenant }, ct)
                    .ConfigureAwait(false);
                TraceLog.Info($"ReplicaMaterializer.Apply — TOMBSTONE {row.Entity}/{row.EntityId} (wire delete) → {tombstoned} row(s) soft-deleted.", "sync.apply");
                written += tombstoned;
                continue;
            }

            if (!TryReadValues(map, row.Payload, out var values))
            {
                TraceLog.Warn($"ReplicaMaterializer.Apply — SKIP {row.Entity}/{row.EntityId}: payload is missing a REQUIRED field (poisoned row; skipped so it cannot stall sync). rawPayload={Truncate(row.Payload)}", "sync.apply");
                continue;
            }
            var args = new List<object>(map.Columns.Length + 6) { row.EntityId, tenant };
            foreach (var col in map.Columns) args.Add(Nullable(values[col.Name]));
            if (map.SearchKey is not null) args.Add(map.SearchKey(values));
            args.Add(updated);
            args.Add(Nullable(row.Version));
            args.Add(row.Deleted ? 1 : 0);
            int affected;
            try
            {
                affected = await db.Database.ExecuteSqlRawAsync(map.UpsertSql, args, ct).ConfigureAwait(false);
                if (affected > 0 && map.Children.Length > 0)
                    await ReplaceChildrenAsync(db, map, row.EntityId, row.Payload, ct).ConfigureAwait(false);
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == SqliteConstraintErrorCode)
            {
                // A business-key clash (e.g. the same SKU / room code already exists locally under a different Id) or a NOT NULL violation.
                // SQLite rolls back only the failing statement, so the batch transaction stays valid: skip the row instead of stalling sync.
                TraceLog.Warn($"ReplicaMaterializer.Apply — SKIP {row.Entity}/{row.EntityId}: local constraint violation ({ex.Message}); the row is still in repl_base.", "sync.apply");
                continue;
            }
            written += affected;
            TraceLog.Info($"ReplicaMaterializer.Apply — UPSERT {row.Entity}/{row.EntityId} → {affected} row(s) written to '{map.Table}' (deleted={row.Deleted}).", "sync.apply");
            if (affected > 0 && !row.Deleted && row.Entity == "Appointment" && TryBuildBookedDraft(tenant, row, values) is { } draft)
                booked.Add(draft);
        }

        if (booked.Count > 0)
        {
            var staged = 0;
            foreach (var draft in booked)
                if (await SyncedActivityFeed.AppendAsync(db, draft, zone, batchId, ct).ConfigureAwait(false)) staged++;
            if (staged > 0) await db.SaveChangesAsync(ct).ConfigureAwait(false);
            TraceLog.Info($"ReplicaMaterializer.Apply — appended {staged} 'booked' activity row(s).", "sync.apply");
        }

        TraceLog.Info($"ReplicaMaterializer.Apply DONE — {written} typed business row(s) written for batch={batchId}.", "sync.apply");
        return written;
    }

    private static string Truncate(string? s) => s is null ? "(null)" : s.Length <= 200 ? s : s[..200] + "…";

    /// <summary>The «booked» feed draft for a synced appointment, attributed to the record's own author; null when the author is unknown.</summary>
    private static ActivityEventDraft? TryBuildBookedDraft(string tenant, ReplicaStaging row, IReadOnlyDictionary<string, object?> v)
    {
        var author = Str(v, "CreatedByUserId");
        if (string.IsNullOrWhiteSpace(author) || v["StartUtc"] is not long startTicks) return null;
        var start = new DateTimeOffset(startTicks, TimeSpan.Zero);
        var occurred = row.UpdatedAtUtc ?? start;
        var evt = new AppointmentBookedEvent(IdGenerator.NewId(occurred), tenant, occurred, row.EntityId,
            Str(v, "PetId")!, Str(v, "OwnerId")!, Str(v, "DoctorUserId"), start, (int)(v["DurationMinutes"] as long? ?? 0));
        return ActivityFeedMapper.Map(evt) is { } draft ? draft with { ActorUserId = author } : null;
    }

    /// <summary>
    /// Raw-SQL parameter for a possibly-absent value. EF Core binds a <see langword="null"/> argument as SQL NULL but has no type mapping
    /// for <see cref="DBNull"/> (that throws), so absence is passed as <see langword="null"/>; the annotation is bridged here, once.
    /// </summary>
    private static object Nullable(object? value) => value!;

    private static string Key(string entity, string id) => entity + "|" + id;

    private static async Task<HashSet<string>> LoadPendingAsync(AnimoraLocalDbContext db, string tenant, CancellationToken ct)
    {
        var pending = await db.Set<PendingCommand>().AsNoTracking()
            .Where(c => c.TenantId == tenant && c.AggregateId != null && Entities.Contains(c.AggregateType)
                        && (c.Status == SettlementState.LocallyPosted || c.Status == SettlementState.PendingSettlement))
            .Select(c => new { c.AggregateType, c.AggregateId })
            .ToListAsync(ct).ConfigureAwait(false);
        return pending.Select(p => Key(p.AggregateType, p.AggregateId!)).ToHashSet(StringComparer.Ordinal);
    }

    private static bool TryReadValues(EntityMap map, string payload, out Dictionary<string, object?> values)
    {
        values = new Dictionary<string, object?>(StringComparer.Ordinal);
        using var doc = JsonDocument.Parse(payload);
        if (doc.RootElement.ValueKind != JsonValueKind.Object) return false;
        foreach (var col in map.Columns)
        {
            var value = ReadColumn(col, doc.RootElement, values);
            if (col.Required && (value is null || value is string { Length: 0 })) return false;
            values[col.Name] = value;
        }

        return true;
    }

    /// <summary>Converts one wire value (looked up by <see cref="Col.Json"/> path) to the SQLite parameter for <paramref name="col"/>.</summary>
    private static object? ReadColumn(Col col, JsonElement scope, IReadOnlyDictionary<string, object?> done)
    {
        var el = Lookup(scope, col.Json);
        var isNull = el.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null;
        return col.Kind switch
        {
            Kind.Text => isNull
                ? col.FallbackColumn is not null && done.TryGetValue(col.FallbackColumn, out var fallback) ? fallback ?? col.Default : col.Default
                : el.ValueKind == JsonValueKind.String ? el.GetString() : el.GetRawText(),
            Kind.Int => isNull ? 0 : ReadInt(el),
            Kind.IntOrNull => isNull ? null : ReadInt(el),
            Kind.Bool => isNull ? 0 : el.ValueKind == JsonValueKind.True || (el.ValueKind == JsonValueKind.Number && el.GetInt64() != 0) ? 1 : 0,
            Kind.Json => ReadJsonArray(el),
            Kind.PascalJson => isNull ? "[]" : ToPascalJson(el),
            Kind.StayItems => isNull ? "[]" : ToStayItemsJson(el),
            Kind.EmptySet => "[]",
            _ => isNull ? null : ReadTicks(el),
        };
    }

    /// <summary>The string value of <paramref name="el"/>, or null when it is absent / not a string (never throws, unlike <c>GetString</c> on Undefined).</summary>
    private static string? Text(JsonElement el) => el.ValueKind == JsonValueKind.String ? el.GetString() : null;

    /// <summary>Walks a dotted camelCase path (<c>dailyRate.minor</c>); returns an Undefined element when any segment is absent.</summary>
    private static JsonElement Lookup(JsonElement scope, string path)
    {
        var current = scope;
        foreach (var segment in path.Split('.'))
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current)) return default;
        }

        return current;
    }

    /// <summary>A wire JSON array (or its text form) stored verbatim; anything else becomes an empty array.</summary>
    private static string ReadJsonArray(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.Array => el.GetRawText(),
        JsonValueKind.String when el.GetString() is { } text && text.TrimStart().StartsWith('[') => text,
        _ => "[]",
    };

    /// <summary>
    /// Re-keys a wire JSON document (camelCase, the wire naming policy) to the PascalCase shape the local JSON columns are serialized with
    /// (<c>new JsonSerializerOptions()</c>, no naming policy), so the local EF value converter deserializes it back into the record.
    /// </summary>
    private static string ToPascalJson(JsonElement el)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) WritePascal(writer, el);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WritePascal(Utf8JsonWriter writer, JsonElement el)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in el.EnumerateObject())
                {
                    var name = property.Name.Length == 0 ? property.Name : char.ToUpperInvariant(property.Name[0]) + property.Name[1..];
                    writer.WritePropertyName(name);
                    WritePascal(writer, property.Value);
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in el.EnumerateArray()) WritePascal(writer, item);
                writer.WriteEndArray();
                break;
            default:
                el.WriteTo(writer);
                break;
        }
    }

    /// <summary>
    /// Builds the local <c>StayItem</c> JSON list from the server's flat <c>BoardingStayItems</c> child images
    /// (<c>quantityScaled</c> = quantity × 1000, money split into minor/currency columns).
    /// </summary>
    private static string ToStayItemsJson(JsonElement items)
    {
        if (items.ValueKind != JsonValueKind.Array) return "[]";
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartArray();
            foreach (var item in items.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                writer.WriteStartObject();
                writer.WriteString("ItemId", Text(Lookup(item, "itemId")) ?? string.Empty);
                writer.WriteNumber("Kind", ReadInt(Lookup(item, "kind")));
                if (Lookup(item, "catalogItemId") is { ValueKind: JsonValueKind.String } catalog) writer.WriteString("CatalogItemId", Text(catalog));
                else writer.WriteNull("CatalogItemId");
                writer.WriteString("Title", Text(Lookup(item, "title")) ?? string.Empty);
                writer.WriteStartObject("Quantity");
                writer.WriteNumber("Value", ReadInt(Lookup(item, "quantityScaled")) / 1000m);
                writer.WriteString("UnitCode", Text(Lookup(item, "quantityUnit")) ?? string.Empty);
                writer.WriteEndObject();
                writer.WriteStartObject("UnitPrice");
                writer.WriteNumber("Minor", ReadInt(Lookup(item, "unitPriceMinor")));
                writer.WriteString("Currency", Text(Lookup(item, "unitPriceCurrency")) ?? DefaultCurrency);
                writer.WriteEndObject();
                var consumed = ReadTicks(Lookup(item, "consumedUtc")) ?? DateTimeOffset.UtcNow.UtcTicks;
                writer.WriteString("ConsumedUtc", new DateTimeOffset(consumed, TimeSpan.Zero));
                writer.WriteString("RecordedByUserId", Text(Lookup(item, "recordedByUserId")) ?? string.Empty);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>
    /// Rewrites a root's flat child rows (e.g. <c>ProductVariants</c>) from the wire array: delete-all + insert, mirroring the server's
    /// owned-row replace semantics. A child with a missing required field is skipped.
    /// </summary>
    private static async Task ReplaceChildrenAsync(AnimoraLocalDbContext db, EntityMap map, string ownerId, string payload, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(payload);
        foreach (var child in map.Children)
        {
            await db.Database.ExecuteSqlRawAsync(child.DeleteSql, new object[] { ownerId }, ct).ConfigureAwait(false);
            if (Lookup(doc.RootElement, child.Json) is not { ValueKind: JsonValueKind.Array } rows) continue;
            foreach (var element in rows.EnumerateArray())
            {
                var done = new Dictionary<string, object?>(StringComparer.Ordinal);
                var args = new List<object> { ownerId };
                var valid = element.ValueKind == JsonValueKind.Object;
                foreach (var col in child.Columns)
                {
                    if (!valid) break;
                    var value = ReadColumn(col, element, done);
                    if (col.Required && (value is null || value is string { Length: 0 })) { valid = false; break; }
                    done[col.Name] = value;
                    args.Add(Nullable(value));
                }

                if (valid) await db.Database.ExecuteSqlRawAsync(child.InsertSql, args, ct).ConfigureAwait(false);
            }
        }
    }

    private static long ReadInt(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.Number => el.GetInt64(),
        JsonValueKind.String when long.TryParse(el.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) => n,
        _ => 0,
    };

    private static long? ReadTicks(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.String when DateTimeOffset.TryParse(el.GetString(), CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var at) => at.UtcTicks,
        JsonValueKind.Number => el.GetInt64(),
        _ => null,
    };

    private static string BuildUpsert(string table, Col[] columns, bool withSearchKey)
    {
        var names = new List<string> { "Id", "TenantId" };
        names.AddRange(columns.Select(c => c.Name));
        if (withSearchKey) names.Add("SearchKey");
        names.AddRange(["UpdatedAt", "Version", "Deleted"]);
        var cols = string.Join(", ", names.Select(n => $"\"{n}\""));
        var ps = string.Join(", ", names.Select((_, i) => "{" + i + "}"));
        var set = string.Join(", ", names.Skip(2).Select(n => $"\"{n}\" = excluded.\"{n}\""));
        return $"INSERT INTO \"{table}\" ({cols}) VALUES ({ps}) ON CONFLICT (\"Id\") DO UPDATE SET {set} WHERE \"{table}\".\"TenantId\" = excluded.\"TenantId\"";
    }

    private static string BuildChildInsert(string table, string ownerColumn, Col[] columns)
    {
        var names = new List<string> { ownerColumn };
        names.AddRange(columns.Select(c => c.Name));
        var cols = string.Join(", ", names.Select(n => $"\"{n}\""));
        var ps = string.Join(", ", names.Select((_, i) => "{" + i + "}"));
        return $"INSERT INTO \"{table}\" ({cols}) VALUES ({ps})";
    }
}
