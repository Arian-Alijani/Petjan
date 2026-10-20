# 10 — Backup System Audit (audit target #13)

Scope: local — `Persistence.Sqlite/Maintenance/LocalStoreMaintenance.cs` (backup / restore / quarantine /
vacuum / stash / rebuilds), `KeySource.cs`, `Client.Services/Diagnostics/MaintenanceScheduler.cs`
(cadence); server — `Persistence.PostgreSql/Operator/DatabaseBackupService.cs`,
`Server/Admin/AdminBackupAdapter.cs`, `Server/Admin/AdminBackupEndpoints.cs`.

**Verification outcome.** Both backup paths are carefully built. Local backup uses SQLite's online
`BackupDatabase` into a destination keyed exactly like the source (an encrypted store yields an encrypted
backup), verifies every copy with `PRAGMA quick_check`, moves the old store to a never-deleted quarantine
before a restore, and carries clinic-config sidecars (A26). Server backup shells out to
`pg_dump --format=custom` with the executable + args fixed from config (never operator input), the secret
passed via `PG*` env vars (never the command line), stderr discarded, a 30-min timeout, a single-flight gate,
operator-cookie + antiforgery + rate-limited download, and a strict generated-name whitelist that makes path
traversal impossible. The findings are completeness/retention/consistency gaps and multi-tenant/DR concerns,
not broken backup mechanics.

## Summary table

| ID | Severity | Class | Title |
|---|---|---|---|
| BAK-01 | High | backup (completeness / DR) | Local backup is the **encrypted** DB but the **encryption key is never in the backup** (key lives in the OS keystore). A backup restored on a new machine / after an OS re-install is unreadable — a `.db` with no key is unrecoverable; there is no documented key-escrow in the backup artifact |
| BAK-02 | High | backup / isolation | Server backup is a **single whole-database `pg_dump`** spanning every tenant; there is no per-tenant backup or restore. A downloaded dump hands the operator **all tenants' PII in one file**, and a restore is all-or-nothing — one tenant cannot be restored without rolling back everyone (cross-tenant bleed in a shared backup, the exact risk audit target #13 names) |
| BAK-03 | Medium | backup (latent / RPO) | Server backups are **manual only** (operator presses the button); there is no scheduler, no PITR / WAL archiving. RPO = time since an operator last clicked. A vendor-hosted SaaS DB with no automatic server backup can lose a full day+ of every tenant's data on host loss |
| BAK-04 | Medium | latent | `MaintenanceScheduler` tracks `_lastBackupUtc` **in memory only**; it resets on every app start. A desktop relaunched often backs up on every launch (churn + prune thrash); one left running never re-evaluates across a wall-clock day boundary correctly after sleep. No persisted "last backup" timestamp |
| BAK-05 | Medium | bug (consistency) | Local **restore does not rebuild finance projections** — it rebuilds stock snapshots (`RebuildStockSnapshotsAsync`) and re-reads settings, but `DailyFinanceFact`/`ProductSalesFact`/`OwnerFinancialSnapshot` come back verbatim from the backup. Combined with SAC-01 (projection vs rebuild divergence), a restored store can show finance facts that no longer match its own ledger |
| BAK-06 | Low | latent | Server `DatabaseBackupService.List` orders by `LastWriteTimeUtc` then prunes `Skip(RetainCount)`; a backup file whose mtime is touched (AV scan, copy-in, filesystem restore) re-sorts to the top and an **older-but-recently-touched** dump can survive while a genuinely newer one is pruned. Name carries a sortable UTC stamp that is not used as the primary prune key |
| BAK-07 | Low | latent | Local `RestoreAsync` / `RestoreStashAsync` use `File.Copy(…, overwrite: false)` after moving the live store to quarantine; if a stray `-wal`/`-shm` (or a prior aborted restore) still sits at `Store.DatabasePath`, the copy throws `IOException` and the restore fails with the live store already quarantined — recoverable but leaves no live DB until the quarantined copy is manually returned |
| BAK-08 | Info | — | Verified sound: encrypted-in/encrypted-out local backup, `quick_check` on every copy, never-delete quarantine, A26 config sidecars; server `pg_dump` with fixed args, secret via env not argv, stderr suppressed, timeout + single-flight, operator-auth + antiforgery + rate limit + strict name whitelist (no traversal), every create/download audited (name+size only) |

---

## BAK-08 (baseline verification — backup mechanics are sound)

- Severity: Info
- Location: `LocalStoreMaintenance.BackupAsync`/`RestoreAsync`/`MoveToQuarantine`;
  `DatabaseBackupService.CreateAsync`/`Resolve`/`IsBackupFileName`; `AdminBackupEndpoints.Download`.
- Evidence:
  - **Local, encrypted & verified:** `source.BackupDatabase(destination)` with the destination opened through
    the same `connectionSetup.Prepare` key path ("an encrypted store yields an encrypted backup"); every backup
    and every restore candidate is gated on `PRAGMA quick_check == "ok"`; a failed check deletes the artifact.
  - **Never-destroy restore:** `MoveToQuarantine` moves the live `.db`/`-wal`/`-shm` into
    `<data>/quarantine/<stamp>/` (it "Never deletes"); quarantine-and-rebuild is single-flight
    (`Interlocked.CompareExchange(ref _rebuilding …)`).
  - **Server hardening:** `DatabaseBackupService` fixes the executable + argument list from config, passes
    host/db/user/password/sslmode via `psi.Environment["PG*"]` (never argv), drains+discards stderr/stdout,
    `CancelAfter(30 min)`, `SemaphoreSlim Gate(1,1)` single-flight; `IsBackupFileName` enforces
    `^animora-\d{8}T\d{6}Z-[0-9a-f]{8}\.dump$` (≤64 chars, no separators) and `Resolve` re-checks `File.Exists`
    under `Path.GetFullPath(directory)` — path traversal is structurally impossible.
  - **Endpoint auth:** `/admin/backup/download` is **POST** with `RequireAuthorization(OperatorCookie.Policy)`,
    `ValidateRequestAsync` (antiforgery), `RequireRateLimiting(Operator)`, `CacheControl=no-store`; create +
    download are written to the append-only operator audit (file name + size only, no connection details).
- Why it matters: confirms the transport, encryption-in-transit-to-disk, auth and anti-traversal are correct,
  so the findings are DR/retention/multi-tenant design gaps rather than exploitable mechanics.
- Confidence: High.

## BAK-01 Local backup omits the key needed to restore it

- Severity: High
- Class: backup (completeness / disaster recovery)
- Location: `LocalStoreMaintenance.BackupAsync` + `Open` (keyed via `connectionSetup.Prepare`), `KeySource`
  (`OsKeystore` / `InsecureFallback`).
- Evidence: `BackupAsync` comments "Destination is keyed like the source, so an encrypted store yields an
  encrypted backup." The key itself comes from the OS keystore (`KeySource.OsKeystore`) and is **not** written
  into, or alongside, the `.db` artifact. The backup is therefore a ciphertext blob whose key lives only in the
  keystore of the machine that made it.
- Why it is a problem: the canonical DR scenario — the clinic's PC dies / Windows is re-installed / the backup
  is moved to a new machine — produces an **unrestorable** backup: the `.db` is encrypted, the keystore entry
  is gone with the old OS profile, and nothing in the backup set carries the key or a passphrase-wrapped copy
  of it. A backup you cannot restore after the event you were backing up against is not a backup.
- Scenario / impact: a clinic loses its machine, copies the latest `animora-*.db` to a new PC, and discovers it
  cannot open it. The offline-first product's local store is the owner's only copy of anything not yet synced.
  There is no escrow/export-key flow in this layer.
- Confidence: High (code + key model); the only mitigation would be elsewhere (server-side re-pull), which does
  not cover unsynced local data.

## BAK-02 Server backup is whole-DB; no per-tenant isolation in the artifact

- Severity: High
- Class: backup / isolation
- Location: `DatabaseBackupService.CreateAsync` (`pg_dump … --format=custom`), `AdminBackupAdapter`.
- Evidence: the dump is a full logical backup of `cs.Database` with no table/tenant filter; the comment calls
  it "Full logical backup of the server database." Restore (per the referenced
  `docs/release/03-backup-restore-hostmove.md`) is `pg_restore` of the whole dump.
- Why it is a problem: every tenant's rows — identity, invoices, PII — are in one file that any authenticated
  **operator** can create and download. The isolation audit (report 08) shows strong *runtime* tenant filtering,
  but that wall does not exist in the backup artifact: a single downloaded `.dump` is a cross-tenant data
  export. A restore is also all-or-nothing: you cannot roll one tenant back to yesterday without rolling back
  every other tenant with it.
- Scenario / impact: (a) a leaked/stolen backup file exposes all tenants at once; (b) a single tenant's
  corruption cannot be repaired from backup without a global rollback that loses every other tenant's
  intervening data. For a commercial multi-tenant product this is the central backup-isolation risk.
- Confidence: High (by design; flagged because audit target #13 explicitly requires reviewing cross-tenant
  bleed in a shared backup).

## BAK-03 No automatic/scheduled server backup, no PITR

- Severity: Medium
- Class: backup (latent / RPO)
- Location: `AdminBackupAdapter.CreateAsync` (invoked only from the operator UI with `form.Confirmed`);
  no server `BackgroundService` references `DatabaseBackupService` (grep: only the adapter + the endpoints).
- Evidence: the only caller of `DatabaseBackupService.CreateAsync` is `AdminBackupAdapter.CreateAsync`, which
  requires `form.Confirmed` — an explicit operator action. There is no scheduler, no WAL archiving / continuous
  archiving, no base-backup rotation.
- Why it is a problem: the vendor-hosted central DB's recovery point is whatever an operator last clicked. On
  host loss the exposure is every tenant's data since the last manual dump (potentially days). `pg_dump` is a
  point-in-time logical snapshot with no PITR between dumps.
- Scenario / impact: operator forgets for a week → host dies → a week of every clinic's invoices/records gone.
  For a paid SaaS this is a serious durability gap. Mitigation: scheduled dumps + WAL archiving (PITR).
- Confidence: High (absence of any scheduled caller is verifiable).

## BAK-04 Local backup cadence state is in-memory only

- Severity: Medium
- Class: latent
- Location: `Client.Services/Diagnostics/MaintenanceScheduler.cs`:
  ```csharp
  private DateTimeOffset? _lastBackupUtc;
  using var timer = new PeriodicTimer(TimeSpan.FromMinutes(15));
  if (_lastBackupUtc is null || now - _lastBackupUtc >= TimeSpan.FromDays(1)) { await RunOneAsync(Backup…); }
  ```
- Why it is a problem: `_lastBackupUtc` is a field that starts `null` on every process start, so the first
  15-minute tick after each launch always backs up (the daily throttle never carries across restarts). A
  machine relaunched many times a day performs a backup + prune on each launch (churn, and the 5/retention
  rotation discards older restore points faster than "daily" implies); conversely the cadence relies on the app
  staying alive and is never reconciled against a persisted "last backup at" (which the `StoreMaintenanceLog`
  could provide). There is no catch-up logic after long sleep either.
- Scenario / impact: unpredictable backup frequency and retention window per usage pattern; a kiosk that
  reboots nightly keeps only ~5 launches' worth of restore points regardless of calendar days.
- Confidence: High (field is not persisted; `StoreMaintenanceLog` exists but is not read back for scheduling).

## BAK-05 Restore rebuilds stock snapshots but not finance projections

- Severity: Medium
- Class: bug (consistency)
- Location: `LocalStoreMaintenance.RestoreAsync`:
  ```csharp
  var snapshots = await RebuildStockSnapshotsAsync(ct);   // stock cache re-proven
  // …no RebuildFinanceProjectionsAsync / RebuildActivityFeedAsync…
  ```
- Why it is a problem: the comment "a restored backup may predate or disagree with the snapshot cache — re-prove
  it from the ledger" is applied to **stock only**. The finance facts (`DailyFinanceFact`, `ProductSalesFact`,
  `OwnerFinancialSnapshot`) and the activity feed are trusted as restored. If the backup's facts were written by
  an older/buggier projection (and given SAC-01, the void path already diverges), the restored dashboard shows
  finance numbers that do not match the restored ledger, and nothing re-proves them.
- Scenario / impact: restore an older backup → «آمار»/dashboard revenue/refund/COGS reflect stale or divergent
  facts until an unrelated event happens to touch each day. Fix is one-line symmetry:
  `RebuildFinanceProjectionsAsync` on restore, like stock.
- Confidence: High (code shows stock-only rebuild on the restore path).

## BAK-06 Prune orders by mtime, not by the backup's own timestamp

- Severity: Low
- Class: latent
- Location: `DatabaseBackupService.List` + `Prune`:
  ```csharp
  .OrderByDescending(f => f.LastWriteTimeUtc).ThenByDescending(f => f.Name, …)
  …
  foreach (var stale in List(directory).Skip(RetainCount)) TryDelete(…);
  ```
- Why it is a problem: the file name already carries a sortable UTC stamp (`animora-YYYYMMDDTHHMMSSZ-…`), but
  prune ranks by filesystem `LastWriteTimeUtc` first. Any operation that touches a file's mtime (AV scan,
  backup-of-backup, a filesystem-level restore, `cp -p` gone wrong) re-ranks it as "newest" and can cause a
  genuinely newer dump to fall past `RetainCount` and be deleted while an older, recently-touched one survives.
- Scenario / impact: retention keeps the wrong 5 files after any mtime-touching maintenance; silent until
  someone needs the dump that was pruned. Cheap fix: sort by the name's embedded timestamp as the primary key.
- Confidence: Medium (depends on an external mtime touch; the ordering choice is certain).

## BAK-07 Restore copy with overwrite:false can strand the live store

- Severity: Low
- Class: latent
- Location: `LocalStoreMaintenance.RestoreAsync` (`File.Copy(backupPath, Store.DatabasePath, overwrite: false)`)
  and `RestoreStashAsync` (same pattern); `MoveToQuarantine` only moves files that `File.Exists`.
- Why it is a problem: the design relies on `MoveToQuarantine` having cleared `Store.DatabasePath` (+`-wal`/
  `-shm`) before the `overwrite: false` copy. If a prior aborted restore, an external process, or a leftover
  WAL recreated a file at the destination between the move and the copy, `File.Copy(overwrite:false)` throws
  `IOException` and the restore fails — with the live store already in quarantine. The catch returns
  `STORE_RESTORE_FAILED` and names the quarantine folder, so data is not lost, but the app is left with **no
  live store** until the operator manually returns the quarantined copy.
- Scenario / impact: a rare race/leftover turns a restore into "no database present, manual recovery needed."
  Using `overwrite: true` (the destination was just quarantined and should be empty) or deleting any residual
  destination files first would make the step idempotent.
- Confidence: Medium (needs the residual-file precondition; the failure mode is deterministic once it holds).
