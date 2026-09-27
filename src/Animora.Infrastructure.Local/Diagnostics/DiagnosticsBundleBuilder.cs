using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Animora.Application.Diagnostics;
using Animora.Application.UseCases.Sync;
using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Infrastructure.Local.Options;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Entities;
using Animora.Persistence.Sqlite.Maintenance;
using Animora.Persistence.Sqlite.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Animora.Infrastructure.Local.Diagnostics;

/// <summary>Builds the six-part, payload-free local diagnostics archive.</summary>
public sealed class DiagnosticsBundleBuilder(
    IDbContextFactory<AnimoraLocalDbContext> dbFactory,
    IOptions<LocalStoreOptions> storeOptions,
    IOptions<LocalLoggingOptions> loggingOptions) : IDiagnosticsBundleBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public async Task<Result<string>> BuildAsync(CancellationToken ct = default)
    {
        string? temporaryPath = null;
        try
        {
            var dataDirectory = Path.GetFullPath(storeOptions.Value.DataDirectory);
            var diagnosticsDirectory = Path.Combine(dataDirectory, "diagnostics");
            Directory.CreateDirectory(diagnosticsDirectory);
            var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
            var outputPath = UniquePath(diagnosticsDirectory, $"animora-diag-{stamp}", ".zip");
            temporaryPath = outputPath + ".tmp";

            await using var db = await dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
            await db.Database.OpenConnectionAsync(ct).ConfigureAwait(false);
            var appliedMigrations = (await db.Database.GetAppliedMigrationsAsync(ct).ConfigureAwait(false)).ToArray();
            var nativeVersion = await ReadScalarAsync(db, "SELECT sqlite_version();", ct).ConfigureAwait(false);
            var diagnostics = BuildStoreDiagnostics(storeOptions.Value, appliedMigrations.LastOrDefault() ?? string.Empty,
                nativeVersion);
            var rowCounts = await ReadRowCountsAsync(db, ct).ConfigureAwait(false);
            var pipeline = await ReadPipelineAsync(db, ct).ConfigureAwait(false);
            var auditTail = await ReadAuditTailAsync(db, ct).ConfigureAwait(false);

            await using (var file = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.ReadWrite,
                             FileShare.None, 64 * 1024, FileOptions.Asynchronous))
            using (var archive = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: false))
            {
                await WriteJsonAsync(archive, "environment.json", new
                {
                    Os = RuntimeInformation.OSDescription,
                    Rid = RuntimeInformation.RuntimeIdentifier,
                    AppVersion = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown",
                    LocalStore = diagnostics,
                    NativeSqliteVersion = nativeVersion,
                }, ct).ConfigureAwait(false);
                await WriteJsonAsync(archive, "schema.json", new
                {
                    AppliedMigrations = appliedMigrations,
                    RowCounts = rowCounts,
                }, ct).ConfigureAwait(false);
                await WriteJsonAsync(archive, "pipeline.json", pipeline, ct).ConfigureAwait(false);
                await WriteJsonAsync(archive, "audit-tail.json", auditTail, ct).ConfigureAwait(false);
                archive.CreateEntry("logs/");
                AddNewestLogs(archive, Path.Combine(loggingOptions.Value.DataDirectory, "logs"));
                var redaction = archive.CreateEntry("redaction.txt", CompressionLevel.Fastest);
                await using var redactionStream = redaction.Open();
                await using var writer = new StreamWriter(redactionStream);
                await writer.WriteAsync((
                    "Passwords, hashes, passphrases, tokens, keys, signatures, national IDs, phone numbers, " +
                    "command payloads, and owner/pet personal fields are excluded or redacted. Bundle records contain identifiers only.")
                    .AsMemory(), ct).ConfigureAwait(false);
            }

            File.Move(temporaryPath, outputPath);
            temporaryPath = null;
            return Result<string>.Ok(outputPath);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Result<string>.Fail(ErrorCodes.DiagnosticsBundleFailed, ex.Message);
        }
        finally
        {
            if (temporaryPath is not null)
            {
                try { File.Delete(temporaryPath); }
                catch { /* A failed cleanup must not hide the bundle failure. */ }
            }
        }
    }

    private static LocalStoreDiagnostics BuildStoreDiagnostics(LocalStoreOptions options, string schemaVersion,
        string nativeVersion)
    {
        var database = new FileInfo(options.DatabasePath);
        var backup = Directory.Exists(options.BackupDirectory)
            ? new DirectoryInfo(options.BackupDirectory).EnumerateFiles("*.db")
                .OrderByDescending(file => file.LastWriteTimeUtc).FirstOrDefault()
            : null;
        var keySource = !options.EncryptionEnabled
            ? KeySource.None
            : OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
                ? KeySource.OsKeystore
                : options.AllowInsecureKeyFallback ? KeySource.InsecureFallback : KeySource.None;
        return new LocalStoreDiagnostics(options.DatabasePath, nativeVersion, options.EncryptionEnabled,
            keySource, schemaVersion, false, false, database.Exists ? database.Length : 0,
            backup is null ? null : new DateTimeOffset(backup.LastWriteTimeUtc, TimeSpan.Zero));
    }

    private static async Task<object> ReadPipelineAsync(AnimoraLocalDbContext db, CancellationToken ct)
    {
        var unsettledStates = new[]
        {
            SettlementState.Draft, SettlementState.LocallyPosted, SettlementState.PendingSettlement,
            SettlementState.Rejected, SettlementState.Conflict, SettlementState.NeedsAttention,
        };
        var rows = await db.Set<PendingCommand>().AsNoTracking()
            .Where(command => unsettledStates.Contains(command.Status))
            .OrderByDescending(command => command.CreatedAtUtc)
            .ThenByDescending(command => command.LocalSequence)
            .Take(50)
            .Select(command => new
            {
                command.Id,
                command.OperationId,
                command.TenantId,
                command.UserId,
                command.DeviceId,
                command.CommandType,
                command.AggregateType,
                command.AggregateId,
                command.LocalSequence,
                command.Status,
                command.CausationId,
                command.DependsOnOperationId,
                command.ExpectedAggregateVersion,
                command.CreatedAtUtc,
                command.LastAttemptUtc,
                command.Attempts,
                command.NextAttemptUtc,
                command.LastErrorCode,
                command.FinancialEffect,
                command.ResponsibleRole,
                command.SettledAtUtc,
                command.ServerEntityId,
                command.ServerVersion,
            })
            .ToArrayAsync(ct).ConfigureAwait(false);
        var summary = new PendingCommandSummary(
            await db.Set<PendingCommand>().CountAsync(c => unsettledStates.Contains(c.Status), ct).ConfigureAwait(false),
            await db.Set<PendingCommand>().CountAsync(c => c.Status == SettlementState.PendingSettlement, ct).ConfigureAwait(false),
            await db.Set<PendingCommand>().CountAsync(c => c.Status == SettlementState.Rejected, ct).ConfigureAwait(false),
            await db.Set<PendingCommand>().CountAsync(c => c.Status == SettlementState.Conflict, ct).ConfigureAwait(false),
            await db.Set<PendingCommand>().CountAsync(c => c.Status == SettlementState.NeedsAttention, ct).ConfigureAwait(false),
            await db.Set<PendingCommand>().Where(c => unsettledStates.Contains(c.Status))
                .MinAsync(c => (DateTimeOffset?)c.CreatedAtUtc, ct).ConfigureAwait(false));
        return new { Summary = summary, Commands = rows };
    }

    private static async Task<object[]> ReadAuditTailAsync(AnimoraLocalDbContext db, CancellationToken ct)
    {
        var rows = await db.Set<AuditEntry>().AsNoTracking()
            .OrderByDescending(entry => entry.OccurredAtUtc)
            .ThenByDescending(entry => entry.Id)
            .Take(500)
            .Select(entry => new
            {
                entry.Id,
                entry.TenantId,
                entry.UserId,
                entry.DeviceId,
                entry.OccurredAtUtc,
                entry.Category,
                entry.Action,
                entry.EntityType,
                entry.EntityId,
                entry.OperationId,
                entry.CorrelationId,
                entry.Outcome,
                entry.BeforeRef,
                entry.AfterRef,
                entry.AmountMinor,
                entry.AmountCurrency,
                entry.Detail,
            }).ToArrayAsync(ct).ConfigureAwait(false);
        return rows.Select(entry => (object)new
        {
            entry.Id,
            entry.TenantId,
            entry.UserId,
            entry.DeviceId,
            entry.OccurredAtUtc,
            entry.Category,
            entry.Action,
            entry.EntityType,
            entry.EntityId,
            entry.OperationId,
            entry.CorrelationId,
            entry.Outcome,
            entry.BeforeRef,
            entry.AfterRef,
            entry.AmountMinor,
            entry.AmountCurrency,
            Detail = Truncate(entry.Detail, 256),
        }).ToArray();
    }

    private static async Task<SortedDictionary<string, long>> ReadRowCountsAsync(AnimoraLocalDbContext db,
        CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();
        var names = new List<string>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' " +
                                  "AND name <> '__EFMigrationsHistory' ORDER BY name;";
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false)) names.Add(reader.GetString(0));
        }

        var counts = new SortedDictionary<string, long>(StringComparer.Ordinal);
        foreach (var name in names)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT COUNT(*) FROM \"{name.Replace("\"", "\"\"")}\";";
            counts[name] = Convert.ToInt64(await command.ExecuteScalarAsync(ct).ConfigureAwait(false),
                System.Globalization.CultureInfo.InvariantCulture);
        }
        return counts;
    }

    private static async Task<string> ReadScalarAsync(AnimoraLocalDbContext db, string sql, CancellationToken ct)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(await command.ExecuteScalarAsync(ct).ConfigureAwait(false),
            System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static async Task WriteJsonAsync<T>(ZipArchive archive, string name, T value, CancellationToken ct)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Fastest);
        await using var stream = entry.Open();
        await JsonSerializer.SerializeAsync(stream, value, JsonOptions, ct).ConfigureAwait(false);
    }

    private static void AddNewestLogs(ZipArchive archive, string logDirectory)
    {
        if (!Directory.Exists(logDirectory)) return;
        foreach (var file in new DirectoryInfo(logDirectory).EnumerateFiles("animora-*.log")
                     .OrderByDescending(item => item.LastWriteTimeUtc).Take(3))
            archive.CreateEntryFromFile(file.FullName, $"logs/{file.Name}", CompressionLevel.Fastest);
    }

    private static string UniquePath(string directory, string stem, string extension)
    {
        var path = Path.Combine(directory, stem + extension);
        for (var suffix = 1; File.Exists(path); suffix++) path = Path.Combine(directory, $"{stem}-{suffix}{extension}");
        return path;
    }

    private static string? Truncate(string? value, int length) =>
        value is null || value.Length <= length ? value : value[..length];
}
