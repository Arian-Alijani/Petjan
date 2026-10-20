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
using Microsoft.EntityFrameworkCore.Infrastructure;
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
    // Visits → تنظیمات: the laboratory test-type catalog is clinic configuration of the same kind and travels the same way.
    internal const string LabCatalogSidecarSuffix = ".lab-test-types.json";
    private static readonly string[] BackupSuffixes = [.. SideSuffixes, SettingsSidecarSuffix, LabCatalogSidecarSuffix];

    // Process-wide: a corrupt store detected during a rebuild must not trigger another rebuild.
    private static int _rebuilding;

    private LocalStoreOptions Store => options.Value;

    /// <summary>
    /// Single-tenant store transition. The schema's relational table inventory includes owned/child tables,
    /// replica generations, cursors, sessions and local projections; no hand-maintained business-table list.
    /// Installation identity and global reference catalogs survive. No quarantine retains another clinic's rows.
    /// </summary>
    public Task<Result> EnsureBoundToAsync(string tenantId, CancellationToken ct) =>
        EnsureBoundToAsync(tenantId, preserveOutgoingUnsynced: false, ct);

    public async Task<Result> EnsureBoundToAsync(string tenantId, bool preserveOutgoingUnsynced, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(tenantId) || tenantId.Length > 128 || tenantId == LocalInstallationState.DefaultTenantId)
            return Result.Fail(ErrorCodes.ValidationFailed);
        var state = services.GetRequiredService<LocalInstallationState>();
        await state.TransitionGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            string outgoingTenantId;
            await using (var db = await contexts.CreateDbContextAsync(ct).ConfigureAwait(false))
            {
                var installation = await db.Set<LocalInstallation>().AsTracking().SingleOrDefaultAsync(ct).ConfigureAwait(false);
                if (installation is null) return Result.Fail(ErrorCodes.StoreUnavailable, "installation_not_initialized");
                if (installation.TenantId == tenantId)
                {
                    state.Set(installation);
                    return Result.Ok();
                }
                outgoingTenantId = installation.TenantId;

                // The outgoing clinic still owns the live store. If it has unsynced work, either refuse (default) or — on a
                // confirmed switch — park its WHOLE store under its own tenant key first so returning restores it intact. This
                // guard runs whether or not the incoming clinic has a stash: a file‑level restore would otherwise overwrite the
                // outgoing store and lose its unsynced data.
                var outgoingHasUnsynced = await db.Set<PendingCommand>().IgnoreQueryFilters()
                    .AnyAsync(x => x.Status != SettlementState.Settled && x.Status != SettlementState.Reversed, ct).ConfigureAwait(false);
                if (outgoingHasUnsynced)
                {
                    if (!preserveOutgoingUnsynced || !IsRealTenant(outgoingTenantId))
                    {
                        // Surface the count so the UI can offer an explicit "switch anyway, keep them for later" choice (T3).
                        var pending = await db.Set<PendingCommand>().IgnoreQueryFilters()
                            .CountAsync(x => x.Status != SettlementState.Settled && x.Status != SettlementState.Reversed, ct)
                            .ConfigureAwait(false);
                        return Result.Fail(ErrorCodes.SyncPendingChanges, $"tenant_switch_requires_settlement:{pending}");
                    }
                    // A bad stash aborts the switch — never wipe/overwrite unsynced data without a verified copy of it.
                    var parked = await StashCurrentAsync(db, outgoingTenantId, ct).ConfigureAwait(false);
                    if (parked.IsFailure) return parked;
                }

                // The incoming clinic may have been parked earlier (switch‑away with unsynced data). Returning to it must bring
                // its own store back verbatim — its unsynced commands and local rows — rather than rebinding to an empty clinic.
                // A file‑level restore replaces the whole store, so the destructive table wipe is skipped for this case.
                if (HasStash(tenantId))
                    return await RestoreStashAsync(db, tenantId, state, ct).ConfigureAwait(false);
            }

            return await WipeAndBindAsync(tenantId, state, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsStoreFailure(ex))
        {
            return Result.Fail(Map(ex, ErrorCodes.StoreUnavailable), "tenant_binding_failed");
        }
        finally { state.TransitionGate.Release(); }
    }

    /// <summary>
    /// Destructive single‑tenant transition: clears every business table of the current store (installation identity and the
    /// global permission catalog survive), rebinds the installation and re‑seeds only the Tenant + RoleDefinitions. Assumes the
    /// caller already decided the outgoing clinic's unsynced data is handled (refused or stashed) and holds the transition gate.
    /// </summary>
    private async Task<Result> WipeAndBindAsync(string tenantId, LocalInstallationState state, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct).ConfigureAwait(false);
        var installation = await db.Set<LocalInstallation>().AsTracking().SingleOrDefaultAsync(ct).ConfigureAwait(false);
        if (installation is null) return Result.Fail(ErrorCodes.StoreUnavailable, "installation_not_initialized");

        await db.Database.OpenConnectionAsync(ct).ConfigureAwait(false);
        await db.Database.ExecuteSqlRawAsync("PRAGMA secure_delete=ON;", ct).ConfigureAwait(false);
        await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
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
            await using var deletion = db.Database.GetDbConnection().CreateCommand();
            deletion.Transaction = transaction.GetDbTransaction();
            deletion.CommandText = "DELETE FROM " + db.GetService<ISqlGenerationHelper>().DelimitIdentifier(table) + ";";
            await deletion.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
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
        if (services.GetService<ILabTestCatalogStore>() is { } labCatalog)
            await labCatalog.ResetAsync(ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        // Publish only after durable commit: a failed/cancelled wipe must leave the old filter intact.
        state.Set(installation);
        services.GetService<SessionContextCache>()?.Set(null);
        services.GetService<IClinicTimeZoneProvider>()?.Invalidate();
        return Result.Ok();
    }

    // ── Per‑tenant stash (clinic switch while unsynced data is parked, kept strictly isolated by tenant id) ──────────────────
    // Layout: <DataDirectory>/clinics/<tenantId>/animora.db (+ -wal/-shm + appointment‑settings sidecar). A stash is a complete,
    // separate encrypted database file; one clinic's rows can never surface in another because each tenant reads only its own
    // folder. The latest switch‑away overwrites the previous stash for that tenant (only the newest parked state is kept).

    private string StashRoot => Path.Combine(Store.DataDirectory, "clinics");

    private string StashPath(string tenantId) => Path.Combine(StashRoot, SanitizeTenant(tenantId), "animora.db");

    private static bool IsRealTenant(string tenantId) =>
        !string.IsNullOrWhiteSpace(tenantId) && tenantId != LocalInstallationState.DefaultTenantId;

    private bool HasStash(string tenantId)
    {
        if (!IsRealTenant(tenantId)) return false;
        var path = StashPath(tenantId);
        return File.Exists(path) && QuickCheck(path) == "ok";
    }

    /// <summary>Hot‑copies the live store (and its settings sidecar) into the outgoing tenant's stash, verified with quick_check.</summary>
    private async Task<Result> StashCurrentAsync(AnimoraLocalDbContext db, string outgoingTenantId, CancellationToken ct)
    {
        // Close the live EF connection and drain the pool so BackupDatabase/opens see a consistent, unlocked file.
        await db.Database.CloseConnectionAsync().ConfigureAwait(false);
        SqliteConnection.ClearAllPools();
        var target = StashPath(outgoingTenantId);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        foreach (var suffix in SideSuffixes)
        {
            var side = target + suffix;
            if (File.Exists(side)) File.Delete(side); // overwrite any older stash for this tenant
        }
        using (var source = Open(Store.DatabasePath))
        using (var destination = Open(target, sessionPragmas: false))
            source.BackupDatabase(destination);
        var settings = Store.DatabasePath + SettingsSidecarSuffix;
        if (File.Exists(settings)) File.Copy(settings, target + SettingsSidecarSuffix, overwrite: true);
        var check = QuickCheck(target);
        if (check != "ok")
        {
            foreach (var suffix in BackupSuffixes)
                if (File.Exists(target + suffix)) File.Delete(target + suffix);
            return Result.Fail(ErrorCodes.StoreBackupFailed, $"stash quick_check: {check}");
        }
        await LogAsync(StoreMaintenanceKind.Backup, $"stash tenant {outgoingTenantId}", ct).ConfigureAwait(false);
        return Result.Ok();
    }

    /// <summary>
    /// Restores the incoming tenant's parked store over the live file (whole‑file swap), re‑publishes the installation and clears
    /// the derived caches, then removes the consumed stash. The restored installation must already be bound to the incoming
    /// tenant (it was captured at switch‑away); a mismatch aborts without deleting the stash so nothing is lost.
    /// </summary>
    private async Task<Result> RestoreStashAsync(AnimoraLocalDbContext db, string tenantId, LocalInstallationState state, CancellationToken ct)
    {
        var stash = StashPath(tenantId);
        await db.Database.CloseConnectionAsync().ConfigureAwait(false);
        SqliteConnection.ClearAllPools();
        foreach (var suffix in SideSuffixes)
        {
            var side = Store.DatabasePath + suffix;
            if (File.Exists(side)) File.Delete(side);
        }
        File.Copy(stash, Store.DatabasePath, overwrite: false);

        await using (var restored = await contexts.CreateDbContextAsync(ct).ConfigureAwait(false))
        {
            var installation = await restored.Set<LocalInstallation>().AsTracking().SingleOrDefaultAsync(ct).ConfigureAwait(false);
            if (installation is null || installation.TenantId != tenantId)
                return Result.Fail(ErrorCodes.StoreCorrupt, "restored_stash_tenant_mismatch");
            state.Set(installation);
        }
        // The appointment‑settings sidecar is clinic customization that travels with the store (A26): import the parked copy
        // (quarantining the current one) so the restored clinic's calendar mode / custom types come back; absent = defaults.
        var settingsStash = stash + SettingsSidecarSuffix;
        if (File.Exists(settingsStash) && services.GetService<IAppointmentSettingsStore>() is { } appointmentSettings)
            await appointmentSettings.ImportFromAsync(settingsStash, ct).ConfigureAwait(false);
        else if (services.GetService<IAppointmentSettingsStore>() is { } reset)
            await reset.SaveAsync(Animora.Contracts.Appointments.AppointmentSettings.Default, ct).ConfigureAwait(false);
        services.GetService<SessionContextCache>()?.Set(null);
        services.GetService<IClinicTimeZoneProvider>()?.Invalidate();

        var folder = Path.GetDirectoryName(stash)!;
        try { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        await LogAsync(StoreMaintenanceKind.Restore, $"stash tenant {tenantId}", ct).ConfigureAwait(false);
        return Result.Ok();
    }

    private static string SanitizeTenant(string tenantId)
    {
        // Tenant ids are ULIDs (A–Z0–9), but be defensive: never let a crafted id escape the clinics root.
        Span<char> buffer = stackalloc char[tenantId.Length];
        for (var i = 0; i < tenantId.Length; i++)
        {
            var c = tenantId[i];
            buffer[i] = char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_';
        }
        return new string(buffer);
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

    /// <inheritdoc />
    public async Task<Result> ResetForResyncAsync(CancellationToken ct)
    {
        var state = services.GetRequiredService<LocalInstallationState>();
        await state.TransitionGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            string currentTenantId;
            await using (var db = await contexts.CreateDbContextAsync(ct).ConfigureAwait(false))
            {
                var installation = await db.Set<LocalInstallation>().AsNoTracking().SingleOrDefaultAsync(ct).ConfigureAwait(false);
                if (installation is null) return Result.Fail(ErrorCodes.StoreUnavailable, "installation_not_initialized");
                currentTenantId = installation.TenantId;
            }
            // Nothing to re-pull before a clinic is bound (the default tenant has no server-side snapshot).
            if (!IsRealTenant(currentTenantId))
                return Result.Fail(ErrorCodes.StoreUnavailable, "reset_requires_bound_clinic");

            // Same destructive wipe used by a clinic switch, but rebinding to the SAME tenant: it clears every business table plus
            // the replica base/checkpoint/staging and the sync cursor (all are relational tables in the model), keeping only the
            // installation identity + permission catalog, and re-seeds Tenant + RoleDefinitions. No stash: this is an explicit wipe.
            var wiped = await WipeAndBindAsync(currentTenantId, state, ct).ConfigureAwait(false);
            if (wiped.IsFailure) return wiped;

            await LogAsync(StoreMaintenanceKind.Quarantine, $"reset-for-resync tenant {currentTenantId}", ct).ConfigureAwait(false);
            return Result.Ok();
        }
        catch (Exception ex) when (IsStoreFailure(ex))
        {
            return Result.Fail(Map(ex, ErrorCodes.StoreUnavailable), "reset_for_resync_failed");
        }
        finally { state.TransitionGate.Release(); }
    }

    public async Task<Result<CompactionSummary>> CompactAsync(RetentionPolicy? policy, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        policy ??= RetentionPolicy.From(Store);
        if (policy.OutboxRetentionDays < 1 || policy.ActivityRetentionDays < 1 || policy.BatchSize < 1)
            return Result<CompactionSummary>.Fail(ErrorCodes.ValidationFailed, "Retention windows and batch size must be positive.");
        try
        {
            var now = clock.UtcNow;
            // The feed is rebuilt from the outbox, so the outbox must never be pruned below the activity window.
            var outboxDays = Math.Max(policy.OutboxRetentionDays, policy.ActivityRetentionDays);
            var outboxCutoff = now.AddDays(-outboxDays).UtcTicks;
            var activityCutoff = now.AddDays(-policy.ActivityRetentionDays).UtcTicks;

            // Only fully relayed rows: Pending/Retry/Claimed/Processing are live work and DeadLettered is evidence (never silently dropped).
            var outbox = await DeleteInBatchesAsync(
                "DELETE FROM OutboxMessages WHERE Id IN (SELECT Id FROM OutboxMessages WHERE Status = {0} AND ProcessedAtUtc IS NOT NULL AND ProcessedAtUtc < {1} LIMIT {2});",
                [(int)OutboxStatus.Succeeded, outboxCutoff], policy.BatchSize, ct);
            var activity = await DeleteInBatchesAsync(
                "DELETE FROM ActivityEvents WHERE Id IN (SELECT Id FROM ActivityEvents WHERE OccurredUtc < {0} LIMIT {1});",
                [activityCutoff], policy.BatchSize, ct);

            var summary = new CompactionSummary(outbox, activity);
            await LogAsync(StoreMaintenanceKind.Compact, $"outbox: {outbox}, activity: {activity}", ct);
            return Result<CompactionSummary>.Ok(summary);
        }
        catch (Exception ex) when (IsStoreFailure(ex))
        {
            return Result<CompactionSummary>.Fail(Map(ex, ErrorCodes.StoreUnavailable), ex.Message);
        }
    }

    // Each statement is its own short atomic transaction (bounded by LIMIT), so a large backlog never holds one huge write lock
    // and a cancellation/crash between batches leaves a consistent, simply-less-compacted store.
    private async Task<int> DeleteInBatchesAsync(string sql, object[] leadingArgs, int batchSize, CancellationToken ct)
    {
        var args = new object[leadingArgs.Length + 1];
        leadingArgs.CopyTo(args, 0);
        args[^1] = batchSize;
        var total = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            await using var context = await contexts.CreateDbContextAsync(ct);
            var removed = await context.Database.ExecuteSqlRawAsync(sql, args, ct);
            total += removed;
            if (removed < batchSize) return total;
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
        if (services.GetService<IAppointmentSettingsStore>() is { } store)
        {
            try { await store.ExportToAsync(target + SettingsSidecarSuffix, ct); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }

        if (services.GetService<ILabTestCatalogStore>() is { } labCatalog)
        {
            try { await labCatalog.ExportToAsync(target + LabCatalogSidecarSuffix, ct); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private async Task ImportSettingsAsync(string backupPath, CancellationToken ct)
    {
        var sidecar = backupPath + SettingsSidecarSuffix;
        if (services.GetService<IAppointmentSettingsStore>() is { } store && File.Exists(sidecar))
        {
            try { await store.ImportFromAsync(sidecar, ct); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }

        var labSidecar = backupPath + LabCatalogSidecarSuffix;
        if (services.GetService<ILabTestCatalogStore>() is { } labCatalog && File.Exists(labSidecar))
        {
            try { await labCatalog.ImportFromAsync(labSidecar, ct); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
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
