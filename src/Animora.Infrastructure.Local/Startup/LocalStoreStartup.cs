using System.Globalization;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Domain.Time;
using Animora.Infrastructure.Local.Security;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Entities;
using Animora.Persistence.Sqlite.Maintenance;
using Animora.Persistence.Sqlite.Options;
using Animora.Persistence.Sqlite.Seed;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Animora.Infrastructure.Local.Startup;

/// <summary>
/// The 8-step local store startup. Runs in its own DI scope and must never run on the UI thread
/// (PERFORMANCE §12: the desktop starts it as a background task, C01). No network (A-RULE 016).
/// </summary>
public sealed class LocalStoreStartup(
    IOptions<LocalStoreOptions> options,
    IServiceScopeFactory scopes,
    LocalDbKeyService keys,
    InstallationIdentity identity,
    IClock clock,
    ILogger<LocalStoreStartup> logger) : ILocalStoreInitializer
{
    public const string StepDataDirectory = "1-data-directory";
    public const string StepOptionsAndKey = "2-options-and-key";
    public const string StepOpenAndAssertNative = "3-open-native-assert";
    public const string StepIntegrityCheck = "4-quick-check";
    public const string StepMigrate = "5-migrate";
    public const string StepCheckpoint = "6-checkpoint";
    public const string StepBootstrapAndSeed = "7-bootstrap-seed";
    public const string StepDiagnostics = "8-diagnostics";

    // Set while QuarantineAndRebuild re-enters this initializer: a still-corrupt store must not quarantine again.
    private static readonly AsyncLocal<bool> RebuildInProgress = new();

    private readonly List<string> _steps = [];

    /// <summary>Names of the steps executed by the last <see cref="InitializeAsync"/> call, in order (A12).</summary>
    internal IReadOnlyList<string> Steps => _steps;

    private LocalStoreOptions Store => options.Value;

    public async Task<Result<LocalStoreDiagnostics>> InitializeAsync(CancellationToken ct)
    {
        _steps.Clear();
        ct.ThrowIfCancellationRequested();
        var dbPath = Path.GetFullPath(Store.DatabasePath);
        try
        {
            // 1. Data directory (user-only) + crash marker, before anything is opened.
            _steps.Add(StepDataDirectory);
            EnsurePrivateDirectory(Store.DataDirectory);
            var wal = new FileInfo(dbPath + "-wal");
            var recovered = wal.Exists && wal.Length > 0;

            // 2. Options, then the key — a key failure returns before any store file exists.
            _steps.Add(StepOptionsAndKey);
            var valid = Store.Validate();
            if (valid.IsFailure) return Result<LocalStoreDiagnostics>.Fail(valid.Error, valid.Detail);
            if (Store.EncryptionEnabled)
            {
                var key = keys.GetOrCreatePassphrase();
                if (key.IsFailure) return Result<LocalStoreDiagnostics>.Fail(ErrorCodes.StoreKeyUnavailable, key.Detail);
                // SQL-01: fail closed BEFORE any file is touched — never let a missing cipher bundle degrade to plaintext.
                if (!StoreCipherProbe.IsCipherAvailable())
                    return Result<LocalStoreDiagnostics>.Fail(ErrorCodes.StoreEncryptionNotApproved,
                        "Encryption is enabled but the native cipher bundle (SQLite3 Multiple Ciphers) is not loaded.");
            }
            else if (!Store.PlaintextStoreAcknowledged)
            {
                // SQL-01: a shipped configuration must never be silently plaintext; make it impossible to miss.
                logger.LogWarning(
                    "LOCAL STORE IS NOT ENCRYPTED AT REST ({Decision}). PII and financial data in '{Path}' are readable " +
                    "by any process with file access. Ship the SQLite3 Multiple Ciphers bundle (STACK.md §3.3, gate K2).",
                    Store.EncryptionDecision, dbPath);
            }

            string nativeVersion;
            using (var scope = scopes.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AnimoraLocalDbContext>();
                bool corrupt;

                // 3. Open (interceptor: key + pragmas) and assert the loaded native SQLite before any migration.
                _steps.Add(StepOpenAndAssertNative);
                try
                {
                    await db.Database.OpenConnectionAsync(ct);
                    var connection = (SqliteConnection)db.Database.GetDbConnection();
                    var native = NativeSqliteAssertion.Assert(connection);
                    if (native.IsFailure) return Result<LocalStoreDiagnostics>.Fail(native.Error, native.Detail);
                    nativeVersion = connection.ServerVersion;

                    // 4. Integrity.
                    _steps.Add(StepIntegrityCheck);
                    corrupt = !string.Equals(Scalar(connection, "PRAGMA quick_check;"), "ok", StringComparison.Ordinal);
                }
                catch (SqliteException ex) when (ex.SqliteErrorCode is 11 or 26) // SQLITE_CORRUPT / SQLITE_NOTADB
                {
                    if (!_steps.Contains(StepIntegrityCheck)) _steps.Add(StepIntegrityCheck);
                    corrupt = true;
                    nativeVersion = string.Empty;
                }

                if (corrupt)
                {
                    await db.Database.CloseConnectionAsync();
                    if (RebuildInProgress.Value)
                        return Result<LocalStoreDiagnostics>.Fail(ErrorCodes.StoreCorrupt, "Store corrupt after rebuild.");
                    return await QuarantineAsync(ct);
                }

                // 5. Schema.
                _steps.Add(StepMigrate);
                await db.Database.MigrateAsync(ct);

                // 6. WAL checkpoint when large; record crash recovery.
                _steps.Add(StepCheckpoint);
                await CheckpointAsync(db, dbPath, recovered, ct);

                // 7. Installation row, then first-run reference data.
                _steps.Add(StepBootstrapAndSeed);
                var installation = await scope.ServiceProvider.GetRequiredService<LocalInstallationBootstrap>()
                    .EnsureAsync(identity.ComputeMachineFingerprintHash(), ct);
                if (installation.IsFailure)
                    return Result<LocalStoreDiagnostics>.Fail(installation.Error, installation.Detail);
                // A server-bound replica must never re-seed the default clinic or sample data on restart.
                if (installation.Value!.TenantId == LocalInstallationState.DefaultTenantId)
                {
                    var seeded = await scope.ServiceProvider.GetRequiredService<LocalSeeder>().SeedAsync(ct);
                    if (seeded.IsFailure) return Result<LocalStoreDiagnostics>.Fail(seeded.Error, seeded.Detail);
                }

                // 8. Diagnostics.
                _steps.Add(StepDiagnostics);
                var migration = (await db.Database.GetAppliedMigrationsAsync(ct)).LastOrDefault() ?? string.Empty;
                var lastBackup = await db.Set<StoreMaintenanceLog>().AsNoTracking()
                    .Where(e => e.Kind == StoreMaintenanceKind.Backup)
                    .OrderByDescending(e => e.OccurredAtUtc)
                    .Select(e => (DateTimeOffset?)e.OccurredAtUtc)
                    .FirstOrDefaultAsync(ct);
                await db.Database.CloseConnectionAsync();

                return Result<LocalStoreDiagnostics>.Ok(new LocalStoreDiagnostics(
                    dbPath, nativeVersion, Store.EncryptionEnabled, keys.Source, migration, recovered,
                    Quarantined: false, File.Exists(dbPath) ? new FileInfo(dbPath).Length : 0, lastBackup));
            }
        }
        catch (Exception ex) when (IsStoreFailure(ex))
        {
            var code = ex is InvalidOperationException ? ErrorCodes.StoreKeyUnavailable : ErrorCodes.StoreUnavailable;
            return Result<LocalStoreDiagnostics>.Fail(code, ex.Message);
        }
    }

    private async Task<Result<LocalStoreDiagnostics>> QuarantineAsync(CancellationToken ct)
    {
        RebuildInProgress.Value = true;
        try
        {
            using var scope = scopes.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<ILocalStoreMaintenance>().QuarantineAndRebuildAsync(ct);
        }
        finally
        {
            RebuildInProgress.Value = false;
        }
    }

    private async Task CheckpointAsync(AnimoraLocalDbContext db, string dbPath, bool recovered, CancellationToken ct)
    {
        var connection = (SqliteConnection)db.Database.GetDbConnection();
        var wal = new FileInfo(dbPath + "-wal");
        var pageSize = long.Parse(Scalar(connection, "PRAGMA page_size;"), CultureInfo.InvariantCulture);
        if (wal.Exists && pageSize > 0 && wal.Length / pageSize > Store.WalCheckpointPages)
        {
            var pages = wal.Length / pageSize;
            Scalar(connection, "PRAGMA wal_checkpoint(TRUNCATE);");
            await LogAsync(db, StoreMaintenanceKind.Checkpoint, $"wal pages: {pages}", ct);
        }
        if (recovered) await LogAsync(db, StoreMaintenanceKind.CrashRecovery, "non-empty -wal found before open", ct);
    }

    // StoreMaintenanceLog.Create is internal to Persistence; append the row with the same shape.
    private Task LogAsync(AnimoraLocalDbContext db, StoreMaintenanceKind kind, string detail, CancellationToken ct)
    {
        var now = clock.UtcNow;
        return db.Database.ExecuteSqlRawAsync(
            "INSERT INTO StoreMaintenanceLogs (Id, Kind, OccurredAtUtc, Detail) VALUES ({0}, {1}, {2}, {3});",
            [IdGenerator.NewId(now), (int)kind, now.UtcTicks, detail], ct);
    }

    private static string Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(command.ExecuteScalar(), CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static void EnsurePrivateDirectory(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            RestrictToCurrentUser(Directory.CreateDirectory(path));
            return;
        }
        const UnixFileMode userOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
        if (!Directory.Exists(path)) Directory.CreateDirectory(path, userOnly);
        File.SetUnixFileMode(path, userOnly);
    }

    [SupportedOSPlatform("windows")]
    private static void RestrictToCurrentUser(DirectoryInfo directory)
    {
        var user = WindowsIdentity.GetCurrent().User!;
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None,
            AccessControlType.Allow));
        directory.SetAccessControl(security);
    }

    // Cancellation is never a store failure: it propagates.
    private static bool IsStoreFailure(Exception ex) => ex switch
    {
        SqliteException or IOException or UnauthorizedAccessException or DbUpdateException => true,
        InvalidOperationException io => io.Message.StartsWith(ErrorCodes.StoreKeyUnavailable.Code, StringComparison.Ordinal),
        _ => false,
    };
}
