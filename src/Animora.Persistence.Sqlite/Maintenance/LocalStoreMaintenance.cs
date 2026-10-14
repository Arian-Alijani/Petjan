using System.Globalization;
using Animora.Application.Activity;
using Animora.Application.Time;
using Animora.Persistence.Sqlite.Activity;
using Animora.Application.Ports;
using Animora.Domain.Inventory;
using Animora.Domain.Identity;
using Animora.Domain.Primitives;
using Animora.Application.Security;
using Microsoft.EntityFrameworkCore.Storage;
using Animora.Domain.Results;
using Animora.Domain.Time;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Converters;
using Animora.Persistence.Sqlite.Entities;
using Animora.Persistence.Sqlite.Finance;
using Animora.Persistence.Sqlite.Options;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Animora.Persistence.Sqlite.Maintenance;

/// <summary>
/// Backup / restore / quarantine / vacuum over fresh, unpooled connections that go through the same
/// key + pragma path as EF (<see cref="SqliteConnectionInterceptor.Prepare"/>). Quarantine never deletes.
/// The initializer is resolved lazily from <see cref="IServiceProvider"/> to avoid a DI cycle.
/// </summary>
public sealed class LocalStoreMaintenance(
    IOptions<LocalStoreOptions> options,
    SqliteConnectionInterceptor connectionSetup,
    IDbContextFactory<AnimoraLocalDbContext> contexts,
    IClock clock,
    IServiceProvider services) : ILocalStoreMaintenance
{
    private const string BackupPrefix = "animora-";
    private const string StampFormat = "yyyyMMdd-HHmmss";
    private static readonly string[] SideSuffixes = ["", "-wal", "-shm"];

    // A26: clinic configuration (custom appointment types, calendar mode, SMS intent) lives outside the database, so a
    // backup carries it as a sidecar next to the .db and a restore puts it back.
    internal const string SettingsSidecarSuffix = ".appointment-settings.json";
    private static readonly string[] BackupSuffixes = [.. SideSuffixes, SettingsSidecarSuffix];

    // Process-wide: a corrupt store detected during a rebuild must not trigger another rebuild.
    private static int _rebuilding;

    private LocalStoreOptions Store => options.Value;

    /// <summary>
    /// Single-tenant store transition. The schema's relational table inventory includes owned/child tables,
    /// replica generations, cursors, sessions and local projections; no hand-maintained business-table list.
    /// Installation identity and global reference catalogs survive. No quarantine retains another clinic's rows.
    /// </summary>
    public async Task<Result> EnsureBoundToAsync(string tenantId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(tenantId) || tenantId.Length > 128 || tenantId == LocalInstallationState.DefaultTenantId)
            return Result.Fail(ErrorCodes.ValidationFailed);
        var state = services.GetRequiredService<LocalInstallationState>();
        await state.TransitionGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var db = await contexts.CreateDbContextAsync(ct).ConfigureAwait(false);
            var installation = await db.Set<LocalInstallation>().AsTracking().SingleOrDefaultAsync(ct).ConfigureAwait(false);
            if (installation is null) return Result.Fail(ErrorCodes.StoreUnavailable, "installation_not_initialized");
            if (installation.TenantId == tenantId)
            {
                state.Set(installation);
                return Result.Ok();
            }

            await db.Database.OpenConnectionAsync(ct).ConfigureAwait(false);
            await db.Database.ExecuteSqlRawAsync("PRAGMA secure_delete=ON;", ct).ConfigureAwait(false);
            await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            // Even rejected/conflicted work can contain a user's unresolved intent. Never silently throw it away.
            if (await db.Set<PendingCommand>().IgnoreQueryFilters()
                .AnyAsync(x => x.Status != SettlementState.Settled && x.Status != SettlementState.Reversed, ct).ConfigureAwait(false))
                return Result.Fail(ErrorCodes.SyncPendingChanges, "tenant_switch_requires_settlement");
            await db.Database.ExecuteSqlRawAsync("PRAGMA defer_foreign_keys=ON;", ct).ConfigureAwait(false);
            var preserved = new HashSet<string>(StringComparer.Ordinal)
            {
                db.Model.FindEntityType(typeof(LocalInstallation))!.GetTableName()!,
                db.Model.FindEntityType(typeof(Permission))!.GetTableName()!,
            };
            var tables = db.Model.GetRelationalModel().Tables.Select(t => t.Name)
                .Where(t => !preserved.Contains(t)).ToArray();
            // Trigger-maintained caches go last: deletes of source rows can re-create their generation counters.
            foreach (var table in tables.OrderBy(t => t == "DashboardGenerations" ? 1 : 0))
            {
                var quoted = "\"" + table.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
                await db.Database.ExecuteSqlRawAsync("DELETE FROM " + quoted + ";", ct).ConfigureAwait(false);
            }
            installation.BindTenant(tenantId);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            // Bootstrap only identity reference data, never sample business data or a fake accounting period.
            // A separate context shares this transaction but already uses the NEW tenant (the cache is still old).
            var boundOptions = new DbContextOptionsBuilder<AnimoraLocalDbContext>()
                .UseSqlite(db.Database.GetDbConnection()).Options;
            await using (var bound = new AnimoraLocalDbContext(boundOptions, new BoundTenantContext(tenantId), clock))
            {
                await bound.Database.UseTransactionAsync(transaction.GetDbTransaction(), ct).ConfigureAwait(false);
                var tenant = Tenant.CreateLocalDefault("Animora Clinic");
                bound.Entry(tenant).Property(x => x.Id).CurrentValue = tenantId;
                tenant.TenantId = tenantId;
                bound.Add(tenant);
                bound.AddRange(Enum.GetValues<StaffRole>().Select(r => RoleDefinition.CreateSystem(tenantId, r, clock)));
                await bound.SaveChangesAsync(ct).ConfigureAwait(false);
            }
            // The sidecar is not tenant-filtered; erase its cache and persisted clinic customization as well.
            if (services.GetService<IAppointmentSettingsStore>() is { } settings)
                await settings.SaveAsync(Animora.Contracts.Appointments.AppointmentSettings.Default, ct).ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            // Publish only after durable commit: a failed/cancelled wipe must leave the old filter intact.
            state.Set(installation);
            services.GetService<SessionContextCache>()?.Set(null);
            services.GetService<IClinicTimeZoneProvider>()?.Invalidate();
            return Result.Ok();
        }
        catch (Exception ex) when (IsStoreFailure(ex))
        {
            return Result.Fail(Map(ex, ErrorCodes.StoreUnavailable), "tenant_binding_failed");
        }
        finally { state.TransitionGate.Release(); }
    }

    private sealed class BoundTenantContext(string tenantId) : ILocalTenantContext
    {
        public string TenantId => tenantId;
    }

    public async Task<Result<string>> BackupAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        string? target = null;
        try
        {
            Directory.CreateDirectory(Store.BackupDirectory);
            target = UniquePath(Store.BackupDirectory, BackupPrefix + Stamp(), ".db");
            using (var source = Open(Store.DatabasePath))
            using (var destination = Open(target, sessionPragmas: false))
            {
                // Destination is keyed like the source, so an encrypted store yields an encrypted backup.
                source.BackupDatabase(destination);
            }
            ct.ThrowIfCancellationRequested();
            await ExportSettingsAsync(target, ct);
            var check = QuickCheck(target);
            if (check != "ok")
            {
                DeleteBackupFiles(target);
                return Result<string>.Fail(ErrorCodes.StoreBackupFailed, $"quick_check: {check}");
            }
            Prune();
            await LogAsync(StoreMaintenanceKind.Backup, Path.GetFileName(target), ct);
            return Result<string>.Ok(target);
        }
        catch (Exception ex) when (IsStoreFailure(ex))
        {
            if (target is not null) TryDeleteBackupFiles(target);
            return Result<string>.Fail(Map(ex, ErrorCodes.StoreBackupFailed), ex.Message);
        }
    }

    public async Task<Result> RestoreAsync(string backupPath, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(backupPath) || !File.Exists(backupPath))
            return Result.Fail(ErrorCodes.StoreRestoreFailed, "Backup file not found.");
        string? quarantine = null;
        try
        {
            var check = QuickCheck(backupPath);
            if (check != "ok") return Result.Fail(ErrorCodes.StoreRestoreFailed, $"quick_check: {check}");
            ct.ThrowIfCancellationRequested();

            quarantine = MoveToQuarantine();
            File.Copy(backupPath, Store.DatabasePath, overwrite: false);

            var initialized = await InitializeAsync(ct);
            if (initialized.IsFailure)
                return Result.Fail(ErrorCodes.StoreRestoreFailed,
                    $"{initialized.Error.Code}: {initialized.Detail}; previous store kept in {quarantine}");
            // A26: the backup's clinic configuration comes back with its data; the zone is re-read from the restored tenant.
            await ImportSettingsAsync(backupPath, ct);
            services.GetService<IClinicTimeZoneProvider>()?.Invalidate();
            // C06: a restored backup may predate or disagree with the snapshot cache — re-prove it from the ledger.
            var snapshots = await RebuildStockSnapshotsAsync(ct);
            if (snapshots.IsFailure)
                return Result.Fail(ErrorCodes.StoreRestoreFailed,
                    $"{snapshots.Error.Code}: {snapshots.Detail}; previous store kept in {quarantine}");
            await LogAsync(StoreMaintenanceKind.Restore,
                $"from {Path.GetFileName(backupPath)}; previous store in {quarantine}", ct);
            return Result.Ok();
        }
        catch (Exception ex) when (IsStoreFailure(ex))
        {
            var kept = quarantine is null ? string.Empty : $"; previous store kept in {quarantine}";
            return Result.Fail(Map(ex, ErrorCodes.StoreRestoreFailed), ex.Message + kept);
        }
    }

    public async Task<Result<LocalStoreDiagnostics>> QuarantineAndRebuildAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (Interlocked.CompareExchange(ref _rebuilding, 1, 0) != 0)
            return Result<LocalStoreDiagnostics>.Fail(ErrorCodes.StoreCorrupt,
                "Store still corrupt during rebuild; refusing to quarantine again.");
        try
        {
            var quarantine = MoveToQuarantine();
            var initialized = await InitializeAsync(ct);
            if (initialized.IsFailure) return initialized;
            services.GetService<IClinicTimeZoneProvider>()?.Invalidate();
            // C06: the rebuilt store's on-hand cache must equal its (freshly seeded) ledger.
            var snapshots = await RebuildStockSnapshotsAsync(ct);
            if (snapshots.IsFailure) return Result<LocalStoreDiagnostics>.Fail(snapshots.Error, snapshots.Detail);
            await LogAsync(StoreMaintenanceKind.Quarantine, $"moved to {quarantine}", ct);
            return Result<LocalStoreDiagnostics>.Ok(initialized.Value! with { Quarantined = true });
        }
        catch (Exception ex) when (IsStoreFailure(ex))
        {
            return Result<LocalStoreDiagnostics>.Fail(Map(ex, ErrorCodes.StoreUnavailable), ex.Message);
        }
        finally
        {
            Interlocked.Exchange(ref _rebuilding, 0);
        }
    }

    public async Task<Result<int>> VacuumAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            int freed;
            using (var connection = Open(Store.DatabasePath))
            {
                var before = ScalarLong(connection, "PRAGMA page_count;");
                Execute(connection, "VACUUM;");
                Execute(connection, "PRAGMA wal_checkpoint(TRUNCATE);");
                var after = ScalarLong(connection, "PRAGMA page_count;");
                freed = (int)Math.Max(0, before - after);
            }
            await LogAsync(StoreMaintenanceKind.Vacuum, $"pages freed: {freed}", ct);
            return Result<int>.Ok(freed);
        }
        catch (Exception ex) when (IsStoreFailure(ex))
        {
            return Result<int>.Fail(Map(ex, ErrorCodes.StoreUnavailable), ex.Message);
        }
    }

    public async Task<Result<int>> RebuildStockSnapshotsAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            await using var context = await contexts.CreateDbContextAsync(ct);
            await using var transaction = await context.Database.BeginTransactionAsync(ct);
            var tenantId = context.CurrentTenantId;
            await context.Set<StockOnHandSnapshot>().Where(s => s.TenantId == tenantId).ExecuteDeleteAsync(ct);

            // One streaming pass over the tenant-filtered ledger: exact ×1000 sums + latest movement per key.
            var totals = new Dictionary<(string ProductId, string VariantId), (long OnHand, DateTimeOffset At, string Id)>();
            var rows = context.Set<StockMovement>().AsNoTracking()
                .Select(m => new { m.Id, m.ProductId, m.VariantId, m.OccurredUtc, Value = m.Quantity.Value })
                .AsAsyncEnumerable();
            await foreach (var m in rows.WithCancellation(ct))
            {
                var key = (m.ProductId, m.VariantId);
                var scaled = FixedScaleQuantityConverter.ToScaled(m.Value);
                totals[key] = totals.TryGetValue(key, out var t)
                    ? (checked(t.OnHand + scaled), m.OccurredUtc >= t.At ? m.OccurredUtc : t.At, m.OccurredUtc >= t.At ? m.Id : t.Id)
                    : (scaled, m.OccurredUtc, m.Id);
            }

            var set = context.Set<StockOnHandSnapshot>();
            foreach (var (key, total) in totals)
            {
                var snapshot = StockOnHandSnapshot.Create(tenantId, key.ProductId, key.VariantId);
                snapshot.Reset(total.OnHand, total.At, total.Id);
                set.Add(snapshot);
            }
            await context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return Result<int>.Ok(totals.Count);
        }
        catch (Exception ex) when (IsStoreFailure(ex))
        {
            return Result<int>.Fail(Map(ex, ErrorCodes.StoreUnavailable), ex.Message);
        }
    }

    public async Task<Result<int>> RebuildFinanceProjectionsAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            await using var context = await contexts.CreateDbContextAsync(ct);
            await using var transaction = await context.Database.BeginTransactionAsync(ct);
            var tenantId = context.CurrentTenantId;
            var zone = services.GetRequiredService<IClinicTimeZoneProvider>().GetClinicTimeZone();
            var written = await FinanceProjectionRebuilder.RebuildAsync(context, zone, tenantId, ct);
            await context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return Result<int>.Ok(written);
        }
        catch (Exception ex) when (IsStoreFailure(ex))
        {
            return Result<int>.Fail(Map(ex, ErrorCodes.StoreUnavailable), ex.Message);
        }
    }

    public async Task<Result<int>> RebuildActivityFeedAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            await using var context = await contexts.CreateDbContextAsync(ct);
            await using var transaction = await context.Database.BeginTransactionAsync(ct);
            var tenantId = context.CurrentTenantId;
            var zone = services.GetRequiredService<IClinicTimeZoneProvider>().GetClinicTimeZone();
            // Rows projected from another user's synced records carry a "sync:" correlation marker: their source is not
            // in the local outbox, so a rebuild must not destroy them (provenance would be lost for good).
            await context.Set<ActivityEvent>()
                .Where(x => x.TenantId == tenantId && (x.CorrelationId == null || !x.CorrelationId.StartsWith(SyncedActivityFeed.CorrelationPrefix)))
                .ExecuteDeleteAsync(ct);
            var actorNames = new Dictionary<string, string>(StringComparer.Ordinal);

            var written = 0;
            var messages = await context.Set<OutboxMessage>().AsNoTracking()
                .OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id)
                .Select(x => new { x.MessageType, x.PayloadJson, x.CorrelationId, x.ActorUserId })
                .ToListAsync(ct);
            foreach (var message in messages)
            {
                var source = ActivityFeedMapper.Deserialize(message.MessageType, message.PayloadJson);
                if (source is null || source.TenantId != tenantId) continue;
                var draft = ActivityFeedMapper.Map(source);
                if (draft is null) continue;
                if (string.IsNullOrWhiteSpace(draft.ActorName))
                    draft = draft with
                    {
                        ActorName = await ActivityDetailResolver.ResolveActorNameAsync(
                            context, string.IsNullOrWhiteSpace(message.ActorUserId) ? draft.ActorUserId : message.ActorUserId, actorNames, ct),
                    };
                var (detail, search) = await ActivityDetailResolver.ResolveAsync(context, draft.DetailArgsJson, ct);
                context.Set<ActivityEvent>().Add(ActivityFeedWriter.Create(draft, zone,
                    actor: message.ActorUserId, correlationId: message.CorrelationId,
                    detailText: detail, searchText: search));
                written++;
                // Keep tracked projections bounded even for a long-lived outbox.
                if (written % 500 == 0)
                {
                    await context.SaveChangesAsync(ct);
                    context.ChangeTracker.Clear();
                }
            }
            await context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return Result<int>.Ok(written);
        }
        catch (Exception ex) when (IsStoreFailure(ex))
        {
            return Result<int>.Fail(Map(ex, ErrorCodes.StoreUnavailable), ex.Message);
        }
    }

    private async Task<Result<LocalStoreDiagnostics>> InitializeAsync(CancellationToken ct)
    {
        var initializer = services.GetService<ILocalStoreInitializer>();
        return initializer is null
            ? Result<LocalStoreDiagnostics>.Fail(ErrorCodes.StoreUnavailable, "No local store initializer registered.")
            : await initializer.InitializeAsync(ct);
    }

    private SqliteConnection Open(string path, bool sessionPragmas = true)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Pooling = false,
            DefaultTimeout = Math.Max(1, Store.BusyTimeoutMs / 1000),
        }.ToString());
        try
        {
            connection.Open();
            connectionSetup.Prepare(connection, sessionPragmas);
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private string QuickCheck(string path)
    {
        using var connection = Open(path, sessionPragmas: false);
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA quick_check;";
        return command.ExecuteScalar() as string ?? "no result";
    }

    /// <summary>Moves the db and its -wal/-shm into <c>&lt;data&gt;/quarantine/&lt;stamp&gt;/</c>. Never deletes.</summary>
    private string MoveToQuarantine()
    {
        SqliteConnection.ClearAllPools();
        var root = Path.Combine(Store.DataDirectory, "quarantine");
        Directory.CreateDirectory(root);
        var folder = UniquePath(root, Stamp(), string.Empty);
        Directory.CreateDirectory(folder);
        var db = Path.GetFullPath(Store.DatabasePath);
        foreach (var suffix in SideSuffixes)
        {
            var file = db + suffix;
            if (File.Exists(file)) File.Move(file, Path.Combine(folder, Path.GetFileName(file)));
        }
        return folder;
    }

    private void Prune()
    {
        var stale = Directory.GetFiles(Store.BackupDirectory, BackupPrefix + "*.db")
            .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
            .Skip(Store.BackupRetentionCount);
        foreach (var file in stale) DeleteBackupFiles(file);
    }

    // The settings store lives in the client layer; absent (headless/tests) means there is nothing to package.
    // A failure to package never fails the database backup itself - the database is the source of truth.
    private async Task ExportSettingsAsync(string target, CancellationToken ct)
    {
        if (services.GetService<IAppointmentSettingsStore>() is not { } store) return;
        try { await store.ExportToAsync(target + SettingsSidecarSuffix, ct); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private async Task ImportSettingsAsync(string backupPath, CancellationToken ct)
    {
        if (services.GetService<IAppointmentSettingsStore>() is not { } store) return;
        var sidecar = backupPath + SettingsSidecarSuffix;
        if (!File.Exists(sidecar)) return;
        try { await store.ImportFromAsync(sidecar, ct); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static void DeleteBackupFiles(string path)
    {
        foreach (var suffix in BackupSuffixes)
            if (File.Exists(path + suffix)) File.Delete(path + suffix);
    }

    private static void TryDeleteBackupFiles(string path)
    {
        try { DeleteBackupFiles(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private async Task LogAsync(StoreMaintenanceKind kind, string detail, CancellationToken ct)
    {
        await using var context = await contexts.CreateDbContextAsync(ct);
        context.Set<StoreMaintenanceLog>().Add(StoreMaintenanceLog.Create(kind, detail, clock.UtcNow));
        await context.SaveChangesAsync(ct);
    }

    private string Stamp() => clock.UtcNow.UtcDateTime.ToString(StampFormat, CultureInfo.InvariantCulture);

    private static string UniquePath(string directory, string name, string extension)
    {
        var path = Path.Combine(directory, name + extension);
        for (var i = 1; File.Exists(path) || Directory.Exists(path); i++)
            path = Path.Combine(directory, $"{name}-{i}{extension}");
        return path;
    }

    private static long ScalarLong(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    // Cancellation is never a store failure: it propagates.
    private static bool IsStoreFailure(Exception ex) => ex switch
    {
        SqliteException or IOException or UnauthorizedAccessException or DbUpdateException => true,
        InvalidOperationException io => io.Message.StartsWith(ErrorCodes.StoreKeyUnavailable.Code, StringComparison.Ordinal),
        _ => false,
    };

    private static ErrorCode Map(Exception ex, ErrorCode fallback) => ex switch
    {
        InvalidOperationException => ErrorCodes.StoreKeyUnavailable,
        SqliteException { SqliteErrorCode: 11 or 26 } => ErrorCodes.StoreCorrupt, // SQLITE_CORRUPT / SQLITE_NOTADB
        _ => fallback,
    };
}
