using System.Globalization;
using System.Text.Json;
using Animora.Application.Activity;
using Animora.Domain.Events;
using Animora.Domain.Primitives;
using Animora.Persistence.Sqlite.Activity;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Entities;
using Animora.Persistence.Sqlite.Replica;
using Animora.Persistence.Sqlite.Search;
using Microsoft.EntityFrameworkCore;

namespace Animora.Sync.Custom.Replica;

/// <summary>
/// Projects a freshly staged batch from the generic replica (<c>repl_*</c>) onto the typed business tables the screens read, so a record
/// authored by another user of the clinic becomes visible locally (reception cluster: Owner, Pet, Appointment, ReceptionQueueEntry).
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
    private enum Kind { Text, Int, Bool, Time }

    private sealed record Col(string Name, Kind Kind, bool Required = false)
    {
        public string Json { get; } = JsonNamingPolicy.CamelCase.ConvertName(Name);
    }

    private sealed class EntityMap(string entity, string table, Col[] columns, Func<IReadOnlyDictionary<string, object?>, string>? searchKey = null)
    {
        public string Entity { get; } = entity;
        public string Table { get; } = table;
        public Col[] Columns { get; } = columns;
        public Func<IReadOnlyDictionary<string, object?>, string>? SearchKey { get; } = searchKey;

        public string UpsertSql { get; } = BuildUpsert(table, columns, searchKey is not null);
        public string TombstoneSql { get; } =
            $"UPDATE \"{table}\" SET \"Deleted\" = 1, \"UpdatedAt\" = {{0}}, \"Version\" = {{1}} WHERE \"Id\" = {{2}} AND \"TenantId\" = {{3}}";
    }

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
    ];

    private static readonly string[] Entities = Maps.Select(m => m.Entity).ToArray();

    /// <summary>The wire entity names this materializer projects (exposed for tests/diagnostics).</summary>
    internal static IReadOnlyList<string> SupportedEntities => Entities;

    /// <summary>Projects the staged rows of <paramref name="batchId"/>; returns the number of business rows written. Must run inside the caller's transaction.</summary>
    internal static async Task<int> ApplyAsync(
        AnimoraLocalDbContext db, string tenant, string batchId, TimeZoneInfo zone, CancellationToken ct)
    {
        var pending = await LoadPendingAsync(db, tenant, ct).ConfigureAwait(false);
        var rows = await db.Set<ReplicaStaging>().AsNoTracking()
            .Where(x => x.TenantId == tenant && x.BatchId == batchId && Entities.Contains(x.Entity))
            .OrderBy(x => x.PageOrdinal)
            .ToListAsync(ct).ConfigureAwait(false); // materialised: the connection is reused for the writes below

        var written = 0;
        var booked = new List<ActivityEventDraft>();
        var byEntity = Maps.ToDictionary(m => m.Entity, StringComparer.Ordinal);

        foreach (var row in rows)
        {
            if (pending.Contains(Key(row.Entity, row.EntityId))) continue; // local unsettled intent wins; base still holds the server row
            var map = byEntity[row.Entity];
            var updated = (row.UpdatedAtUtc ?? DateTimeOffset.UtcNow).UtcTicks;

            if (row.Payload is null)
            {
                written += await db.Database.ExecuteSqlRawAsync(map.TombstoneSql, new object[] { updated, Nullable(row.Version), row.EntityId, tenant }, ct)
                    .ConfigureAwait(false);
                continue;
            }

            if (!TryReadValues(map, row.Payload, out var values)) continue;
            var args = new List<object>(map.Columns.Length + 6) { row.EntityId, tenant };
            foreach (var col in map.Columns) args.Add(Nullable(values[col.Name]));
            if (map.SearchKey is not null) args.Add(map.SearchKey(values));
            args.Add(updated);
            args.Add(Nullable(row.Version));
            args.Add(row.Deleted ? 1 : 0);
            var affected = await db.Database.ExecuteSqlRawAsync(map.UpsertSql, args, ct).ConfigureAwait(false);
            written += affected;
            if (affected > 0 && !row.Deleted && row.Entity == "Appointment" && TryBuildBookedDraft(tenant, row, values) is { } draft)
                booked.Add(draft);
        }

        if (booked.Count > 0)
        {
            var staged = 0;
            foreach (var draft in booked)
                if (await SyncedActivityFeed.AppendAsync(db, draft, zone, batchId, ct).ConfigureAwait(false)) staged++;
            if (staged > 0) await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return written;
    }

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
            doc.RootElement.TryGetProperty(col.Json, out var el);
            var isNull = el.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null;
            object? value = col.Kind switch
            {
                Kind.Text => isNull ? null : el.ValueKind == JsonValueKind.String ? el.GetString() : el.GetRawText(),
                Kind.Int => isNull ? 0 : ReadInt(el),
                Kind.Bool => isNull ? 0 : el.ValueKind == JsonValueKind.True || (el.ValueKind == JsonValueKind.Number && el.GetInt64() != 0) ? 1 : 0,
                _ => isNull ? null : ReadTicks(el),
            };
            if (col.Required && (value is null || value is string { Length: 0 })) return false;
            values[col.Name] = value;
        }

        return true;
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
}
