using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Animora.Domain.Results;
using Animora.Domain.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Animora.Persistence.PostgreSql.Operator;

/// <summary>One finished per-tenant export on disk.</summary>
public sealed record TenantBackupFile(string FileName, string TenantId, long SizeBytes, DateTimeOffset CreatedUtc, int Tables, long Rows);

/// <summary>
/// BAK-02: per-tenant logical backup (export half). The whole-DB <see cref="DatabaseBackupService"/> dump spans every clinic, so a
/// single download is a cross-tenant PII export and a restore is all-or-nothing. This service produces an artifact that contains
/// exactly ONE tenant's rows.
/// <para><b>Isolation.</b> The table inventory is read from a <see cref="TenantBoundContext"/> model — the same bound, filter-carrying
/// context every other operator tenant read uses — and every query is parameterised on that context's bound tenant id
/// (<c>WHERE "TenantId" = @tenant</c>), never on a caller-supplied value, so a mistyped/hostile argument can only ever select
/// the tenant the context was bound to. Table/column identifiers come from EF model metadata (never operator input).
/// Soft-deleted rows are included on purpose (a backup must be able to reproduce tombstones).</para>
/// <para><b>Artifact.</b> <c>animora-tenant-&lt;TenantId&gt;-&lt;yyyyMMddTHHmmssZ&gt;-&lt;8 hex&gt;.ndjson.gz</c>: gzip'd NDJSON. Line 1 is a versioned
/// header, then one <c>{"t":"&lt;table&gt;","r":{…row_to_json…}}</c> line per row, then a trailer with per-table counts that
/// <see cref="InspectAsync"/> verifies. Credentials-at-rest-in-transit (refresh tokens) and derived/ephemeral stores (outbox, feed
/// batches, upload chunks) are deliberately excluded.</para>
/// <para><b>Import status (documented limitation).</b> This task lands the isolated <i>export</i> plus a verifier. The import half —
/// replaying the NDJSON through a tenant-bound write context so <c>TenantStampingInterceptor</c> enforces the tenant — is the documented
/// follow-up; until then a single-tenant restore is operator-run from this artifact (see docs/release/03-backup-restore-hostmove.md).
/// The existing whole-DB dump remains the full-DR path and is unchanged.</para>
/// </summary>
public static partial class TenantBackupService
{
    public const int FormatVersion = 1;

    /// <summary>How many exports are kept <i>per tenant</i>; older ones are deleted after a successful export.</summary>
    public const int RetainPerTenant = 5;

    [GeneratedRegex(@"^animora-tenant-[0-9A-Za-z]{1,26}-\d{8}T\d{6}Z-[0-9a-f]{8}\.ndjson\.gz$", RegexOptions.CultureInvariant)]
    private static partial Regex FileNamePattern();

    private static readonly SemaphoreSlim Gate = new(1, 1);

    // Ephemeral / credential / derived stores — never part of a tenant handoff artifact.
    private static readonly HashSet<string> ExcludedClrTypes = new(StringComparer.Ordinal)
    {
        "RefreshToken", "FileUploadChunk", "OutboxMessage", "ConsumedMessage",
        "FeedBatchRow", "FeedBatchPageRow", "TenantFeedStateRow", "TenantJournalRow",
    };

    /// <summary>True when <paramref name="fileName"/> is exactly a per-tenant export name this service generates.</summary>
    public static bool IsTenantBackupFileName(string? fileName) => fileName is { Length: <= 128 } && FileNamePattern().IsMatch(fileName);

    /// <summary>Resolves a validated per-tenant export name to its full path inside <paramref name="directory"/>, or null.</summary>
    public static string? Resolve(string directory, string? fileName)
    {
        if (!IsTenantBackupFileName(fileName)) return null;
        var path = Path.Combine(Path.GetFullPath(directory), fileName!);
        return File.Exists(path) ? path : null;
    }

    /// <summary>Newest-first per-tenant exports in <paramref name="directory"/>, ordered by the name-embedded UTC stamp (BAK-06 rule).</summary>
    public static IReadOnlyList<TenantBackupFile> List(string directory, string? tenantId = null)
    {
        if (!Directory.Exists(directory)) return [];
        return new DirectoryInfo(directory).EnumerateFiles("animora-tenant-*.ndjson.gz")
            .Where(f => IsTenantBackupFileName(f.Name))
            .Select(f => (File: f, Parsed: ParseName(f.Name)))
            .Where(x => x.Parsed is not null && (tenantId is null || string.Equals(x.Parsed.Value.TenantId, tenantId, StringComparison.Ordinal)))
            .OrderByDescending(x => x.Parsed!.Value.Stamp).ThenByDescending(x => x.File.Name, StringComparer.Ordinal)
            .Select(x => new TenantBackupFile(x.File.Name, x.Parsed!.Value.TenantId, x.File.Length, x.Parsed.Value.Stamp, 0, 0)).ToArray();
    }

    /// <summary>The tenant id embedded in a per-tenant export name, or null when <paramref name="fileName"/> is not a valid export name.
    /// Public so callers in other assemblies (e.g. the server audit adapter) can tag a download with its tenant without the internal tuple.</summary>
    public static string? ParseTenantId(string? fileName) => fileName is null ? null : ParseName(fileName)?.TenantId;

    internal static (string TenantId, DateTimeOffset Stamp)? ParseName(string fileName)
    {
        if (!IsTenantBackupFileName(fileName)) return null;
        var rest = fileName["animora-tenant-".Length..];
        var firstDash = rest.IndexOf('-');
        if (firstDash <= 0 || rest.Length < firstDash + 1 + 16) return null;
        var tenant = rest[..firstDash];
        var stamp = rest.AsSpan(firstDash + 1, 16);
        return DateTimeOffset.TryParseExact(stamp, "yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed) ? (tenant, parsed) : null;
    }

    /// <summary>Exports exactly <paramref name="tenantId"/>'s rows into <paramref name="directory"/>; typed failure otherwise.</summary>
    public static async Task<Result<TenantBackupFile>> CreateAsync(
        AnimoraOperatorDbContext db, string tenantId, IClock clock, string directory, DateTimeOffset now, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(tenantId) || !TenantIdPattern().IsMatch(tenantId))
            return Result<TenantBackupFile>.Fail(ErrorCodes.ValidationFailed);
        if (!await db.Tenants.AsNoTracking().AnyAsync(t => t.Id == tenantId && !t.Deleted, ct).ConfigureAwait(false))
            return Result<TenantBackupFile>.Fail(ErrorCodes.NotFound);
        if (!await Gate.WaitAsync(0, ct).ConfigureAwait(false)) return Result<TenantBackupFile>.Fail(ErrorCodes.ConcurrencyConflict);

        string? tmp = null;
        try
        {
            var full = Path.GetFullPath(directory);
            if (OperatingSystem.IsWindows()) Directory.CreateDirectory(full);
            else Directory.CreateDirectory(full, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var name = string.Create(CultureInfo.InvariantCulture,
                $"animora-tenant-{tenantId}-{now.UtcDateTime:yyyyMMdd'T'HHmmss'Z'}-{Guid.NewGuid().ToString("N")[..8]}.ndjson.gz");
            var path = Path.Combine(full, name);
            tmp = path + ".partial";

            // The tenant-bound context is the isolation wall: its bound id is the ONLY tenant any query below can name.
            await using var tenant = TenantBoundContext.Create(db, tenantId, clock);
            var boundTenant = tenant.CurrentTenantId;
            var tables = Inventory(tenant.Model);

            var counts = new SortedDictionary<string, long>(StringComparer.Ordinal);
            await using (var file = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            await using (var gzip = new GZipStream(file, CompressionLevel.Optimal))
            await using (var writer = new StreamWriter(gzip, new UTF8Encoding(false)) { NewLine = "\n" })
            {
                await writer.WriteLineAsync(JsonSerializer.Serialize(new
                {
                    kind = "header", format = "animora-tenant-export", version = FormatVersion,
                    tenantId = boundTenant, createdUtc = now.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
                })).ConfigureAwait(false);

                foreach (var table in tables)
                {
                    ct.ThrowIfCancellationRequested();
                    // Identifiers come from EF metadata; the tenant id is a bound parameter.
                    var sql = $"SELECT row_to_json(t)::text AS \"Value\" FROM {table.Quoted} t WHERE t.\"{table.TenantColumn}\" = {{0}}";
                    var label = JsonSerializer.Serialize(table.Name);
                    long n = 0;
                    await foreach (var json in tenant.Database.SqlQueryRaw<string>(sql, boundTenant).AsAsyncEnumerable().WithCancellation(ct).ConfigureAwait(false))
                    {
                        await writer.WriteLineAsync($"{{\"t\":{label},\"r\":{json}}}").ConfigureAwait(false);
                        n++;
                    }
                    counts[table.Name] = n;
                }

                await writer.WriteLineAsync(JsonSerializer.Serialize(new { kind = "end", tables = counts })).ConfigureAwait(false);
            }

            File.Move(tmp, path, overwrite: false);
            tmp = null;
            Prune(full, tenantId);
            var info = new FileInfo(path);
            return Result<TenantBackupFile>.Ok(new(name, tenantId, info.Length, now, counts.Count, counts.Values.Sum()));
        }
        catch (IOException) { return Result<TenantBackupFile>.Fail(ErrorCodes.StoreUnavailable); }
        catch (UnauthorizedAccessException) { return Result<TenantBackupFile>.Fail(ErrorCodes.StoreUnavailable); }
        catch (Npgsql.NpgsqlException) { return Result<TenantBackupFile>.Fail(ErrorCodes.StoreUnavailable); }
        finally
        {
            if (tmp is not null) TryDelete(tmp);
            Gate.Release();
        }
    }

    /// <summary>
    /// Verifies an export end-to-end without touching the database: header/version, every row line is valid JSON, per-table counts match
    /// the trailer, and (when <paramref name="expectedTenantId"/> is given) the header names that tenant. Returns the totals.
    /// </summary>
    public static async Task<Result<TenantBackupFile>> InspectAsync(string path, string? expectedTenantId, CancellationToken ct)
    {
        try
        {
            await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
            await using var gzip = new GZipStream(file, CompressionMode.Decompress);
            using var reader = new StreamReader(gzip, Encoding.UTF8);
            var header = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (header is null) return Result<TenantBackupFile>.Fail(ErrorCodes.ValidationFailed);
            using var h = JsonDocument.Parse(header);
            var root = h.RootElement;
            if (root.GetProperty("kind").GetString() != "header" || root.GetProperty("version").GetInt32() != FormatVersion)
                return Result<TenantBackupFile>.Fail(ErrorCodes.ValidationFailed);
            var tenantId = root.GetProperty("tenantId").GetString() ?? string.Empty;
            if (expectedTenantId is not null && !string.Equals(tenantId, expectedTenantId, StringComparison.Ordinal))
                return Result<TenantBackupFile>.Fail(ErrorCodes.ValidationFailed);

            var seen = new Dictionary<string, long>(StringComparer.Ordinal);
            Dictionary<string, long>? declared = null;
            string? line;
            while ((line = await reader.ReadLineAsync(ct).ConfigureAwait(false)) is not null)
            {
                using var doc = JsonDocument.Parse(line);
                var el = doc.RootElement;
                if (el.TryGetProperty("kind", out var kind) && kind.GetString() == "end")
                {
                    declared = new(StringComparer.Ordinal);
                    foreach (var p in el.GetProperty("tables").EnumerateObject()) declared[p.Name] = p.Value.GetInt64();
                    continue;
                }
                var table = el.GetProperty("t").GetString()!;
                var row = el.GetProperty("r");
                // Defence in depth: every row must belong to the header's tenant.
                if (row.TryGetProperty("TenantId", out var rowTenant) && !string.Equals(rowTenant.GetString(), tenantId, StringComparison.Ordinal))
                    return Result<TenantBackupFile>.Fail(ErrorCodes.ValidationFailed);
                seen[table] = seen.GetValueOrDefault(table) + 1;
            }
            if (declared is null) return Result<TenantBackupFile>.Fail(ErrorCodes.ValidationFailed); // truncated: no trailer
            foreach (var (table, count) in declared)
                if (seen.GetValueOrDefault(table) != count) return Result<TenantBackupFile>.Fail(ErrorCodes.ValidationFailed);

            var info = new FileInfo(path);
            return Result<TenantBackupFile>.Ok(new(info.Name, tenantId, info.Length, ParseName(info.Name)?.Stamp ?? default, declared.Count, declared.Values.Sum()));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return Result<TenantBackupFile>.Fail(ErrorCodes.ValidationFailed);
        }
    }

    [GeneratedRegex(@"^[0-9A-Za-z]{1,26}$", RegexOptions.CultureInvariant)]
    private static partial Regex TenantIdPattern();

    internal sealed record TableRef(string Name, string Quoted, string TenantColumn);

    /// <summary>Every distinct table of the tenant-bound model that carries a <c>TenantId</c> column, minus the excluded stores.</summary>
    internal static IReadOnlyList<TableRef> Inventory(IModel model)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<TableRef>();
        foreach (var entity in model.GetEntityTypes())
        {
            var table = entity.GetTableName();
            if (table is null || entity.IsOwned() || ExcludedClrTypes.Contains(entity.ClrType.Name)) continue;
            var schema = entity.GetSchema();
            var property = entity.FindProperty("TenantId");
            if (property is null) continue;
            var column = property.GetColumnName(StoreObjectIdentifier.Table(table, schema));
            if (column is null) continue;
            var quoted = (schema is null ? string.Empty : Quote(schema) + ".") + Quote(table);
            if (seen.Add(quoted)) result.Add(new TableRef(table, quoted, column.Replace("\"", "\"\"")));
        }
        result.Sort(static (a, b) => string.CompareOrdinal(a.Name, b.Name));
        return result;
    }

    private static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";

    private static void Prune(string directory, string tenantId)
    {
        foreach (var stale in List(directory, tenantId).Skip(RetainPerTenant)) TryDelete(Path.Combine(directory, stale.FileName));
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
