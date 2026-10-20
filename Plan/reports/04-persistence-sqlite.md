# 04 — Local Persistence (SQLite) Audit (`src/Animora.Persistence.Sqlite/**`)

Scope: DbContext config, migrations 001–033, repositories, read queries, seed, maintenance/backup.
Audit targets emphasised: #1/#4/#9 perf (indexes, N+1, tracking, unbounded sets), #3 latent
(unbounded growth, retention), #12 isolation (encryption-at-rest seam on PII).

## Summary table

| ID | Severity | Class | Title |
|---|---|---|---|
| SQL-01 | High | isolation / latent | Local store is **unencrypted by default** (`EncryptionEnabled` defaults to `false`); PII-at-rest encryption is opt-in and the cipher provider is "compiled but unreachable" |
| SQL-02 | High | latent | No retention/compaction for the append-only growth tables (ActivityEvent, OutboxMessage, AuditEntry, SyncCursor history): `Prune()` only trims **backup files**, never rows |
| SQL-03 | Medium | bug (security) | Store passphrase is embedded into `PRAGMA key = '...'` as an escaped SQL literal, not a bound parameter — fragile keying path |
| SQL-04 | Low | perf | No global `QueryTrackingBehavior.NoTracking` default; correctness relies on each query opting into `AsNoTracking` (24 of 27 query files do — 3 do not) |
| SQL-05 | Low | perf | `SqliteConnectionInterceptor.Pragmas()` allocates a fresh `string[]` on every connection open |
| SQL-06 | Info | — | Index coverage is broad (46 configs carry `HasIndex`, hot tables 3–5 each); WAL + `busy_timeout` + `foreign_keys=ON` pragmas correct; payload/length bounds explicit; append-only audit guard present |

---

## SQL-06 (baseline verification — schema/index hygiene is strong)

- Severity: Info
- Location: `Context/AnimoraLocalDbContext.cs`, `Context/SqliteConnectionInterceptor.cs`,
  `Configurations/**`.
- Evidence: 46 configuration files declare `HasIndex`; the hot write/read tables are well-covered —
  `PendingCommand` (5 indexes), `Activity` (5), `Appointment`/`Invoice`/`Pet`/`Owner`/`JournalEntry`/
  `AuditEntry`/`TreatmentPlan` (4 each). Session pragmas are correct: `journal_mode=WAL`,
  `synchronous=NORMAL`, clamped `busy_timeout`, `foreign_keys=ON`. Large-JSON columns
  (`PendingCommand.PayloadJson`, `OutboxMessage.PayloadJson`, audit/capability/handoff JSON) carry
  explicit `HasMaxLength` bounds instead of the generic short-text convention. Rebuildable projections
  (`StockLevel`, stay items, Rx/visit lines, radiology/lab) are correctly `Ignore<>`d, never tables.
- Why it matters: establishes the schema is not under-indexed; the findings below are the residue
  (encryption default, retention, keying path), not a missing-index epidemic.
- Confidence: High.

---

## SQL-01 Local store is unencrypted by default; PII-at-rest encryption is opt-in and dormant

- Severity: High
- Class: isolation (#12) / latent security
- Location: `Context/LocalStoreServiceCollectionExtensions.cs:51–56` (provider selection),
  `Options/LocalStoreOptions.cs` (`EncryptionEnabled` / `K2Approved`),
  `Context/MultipleCiphersEncryptionProvider.cs` (class summary), `Context/NoEncryptionProvider.cs`.
- Evidence:
  ```csharp
  services.AddSingleton<IStoreEncryptionProvider>(provider =>
      /* options.EncryptionEnabled && K2Approved */
          ? new MultipleCiphersEncryptionProvider()
          : new NoEncryptionProvider());
  // LocalStoreOptions:
  public bool EncryptionEnabled { get; set; }   // default false (bool)
  public bool K2Approved { get; set; }           // default false
  ```
  `MultipleCiphersEncryptionProvider` is documented: *"Compiled but unreachable: selected only when
  `EncryptionEnabled && K2Approved`; the native cipher bundle is pinned in E08, not here."*
- Why it is a problem: the product is described as an *"offline-first … LOCAL encrypted EF store"*
  holding owner PII, clinical records and financial data on the clinic's own machine. With both
  flags defaulting to `false`, the shipped default is `NoEncryptionProvider` — the SQLite file is
  plaintext at rest. Encryption only engages if the host explicitly sets `EncryptionEnabled=true`
  **and** `K2Approved=true`, and even then the cipher bundle must be pinned (E08) for the provider to
  function. So "encrypted local store" is an opt-in that is off unless the deployment wires it.
- Scenario / impact: a stolen/copied clinic laptop or a backup of the `.db` file exposes all tenant
  PII and financial history in cleartext, unless the integrator explicitly enabled encryption. This
  is the single highest data-isolation risk in the local layer and must be verified against the
  actual Desktop composition root (T6/T12) — if the Desktop does not set both flags, the default
  ships unencrypted.
- Confidence: High on the default (code fact); Medium on real exposure (depends on whether the
  Desktop/host sets the flags — cross-check in T6/T12).

## SQL-02 No row retention/compaction on append-only growth tables

- Severity: High
- Class: latent (unbounded growth)
- Location: `Maintenance/LocalStoreMaintenance.cs` — `Prune()` (line ~612) and the Rebuild* methods;
  `Activity/ActivityFeedWriter.cs`, `Activity/SyncedActivityFeed.cs`.
- Evidence: `Prune()` operates on **backup files** only:
  ```csharp
  private void Prune() { ... .Skip(Store.BackupRetentionCount); ... DeleteBackupFiles(...); }
  ```
  The maintenance surface is `BackupAsync`, `RestoreAsync`, `VacuumAsync`,
  `RebuildStockSnapshotsAsync`, `RebuildFinanceProjectionsAsync`, `RebuildActivityFeedAsync`,
  `QuarantineAndRebuildAsync`, `ResetForResyncAsync` — **none deletes or caps rows** in the
  monotonically growing tables. Grep for `Retention`/`Max rows`/`Trim`/`cap` in the Activity writers
  returns nothing. The audit sink is deliberately append-only (`Context/AppendOnlyGuard.cs`).
- Why it is a problem: ActivityEvent, OutboxMessage (even after dispatch), AuditEntry, and
  SyncCursor/replica history grow forever on a long-lived offline install. The ground-truth note
  explicitly lists these as "tables that grow unbounded … with no retention/compaction". On a busy
  clinic over years this inflates the local DB, slows WAL checkpoints, lengthens backups, and
  degrades every list query that is not tightly indexed + paged. `VACUUM` reclaims space from
  *deleted* rows, but nothing deletes rows, so `VacuumAsync` cannot help here.
- Scenario / impact: multi-year install → tens of millions of activity/audit/outbox rows → slow
  dashboards, bloated encrypted backups, longer sync-apply. Correct now; degrades monotonically.
  Needs a retention policy (age/row-count cap) for dispatched outbox messages and old activity,
  with audit kept per compliance but at least archived/rolled.
- Confidence: High.

## SQL-03 Store passphrase embedded as a SQL literal in `PRAGMA key`

- Severity: Medium
- Class: bug (security-adjacent robustness)
- Location: `Context/MultipleCiphersEncryptionProvider.cs` — `Apply`.
- Evidence:
  ```csharp
  // PRAGMA does not accept parameters; the passphrase is embedded as an escaped SQL literal.
  Execute(connection, $"PRAGMA key = '{passphrase.Replace("'", "''", StringComparison.Ordinal)}';");
  ```
- Why it is a problem: `PRAGMA key` genuinely cannot be parameterised, so a literal is unavoidable —
  but string-building the key into SQL is fragile: it relies solely on single-quote doubling and
  leaves the plaintext key in a `CommandText` string (and potentially in any SQL trace/log hook on
  the connection). A key containing other SQL-significant bytes, or a logging interceptor on the
  maintenance connection, risks leakage or a malformed pragma. SQLCipher's supported idiom is the
  `PRAGMA key` with a `x'...'` raw-key or `keyspec` passed via the connection string
  (`Password=`/`SqliteConnectionStringBuilder`), which keeps the key out of ad-hoc `CommandText`.
- Scenario / impact: only reachable when encryption is enabled (see SQL-01). When it is, the keying
  path is harder to audit for leakage and depends on escaping correctness. Recommend keying via the
  connection-string/keyspec API rather than interpolated `CommandText`.
- Confidence: Medium (works for typical keys; the concern is robustness + leakage surface).

## SQL-04 No global no-tracking default for read queries

- Severity: Low
- Class: perf
- Location: `Context/AnimoraLocalDbContext.cs` (no
  `optionsBuilder.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)` and no
  `ChangeTracker.QueryTrackingBehavior` set); `Queries/**`.
- Evidence: grep for `QueryTrackingBehavior`/`NoTracking` in the DbContext returns nothing; read
  correctness relies on each query calling `AsNoTracking`. 24 of 27 files in `Queries/` do;
  **3 do not**.
- Why it is a problem: with the default tracking behaviour, any read query that forgets
  `AsNoTracking` materialises tracked entities into the `ChangeTracker`, costing extra allocations
  and identity-map bookkeeping and risking accidental writes if the same context is reused. A
  store-wide `NoTracking` default (with explicit `AsTracking()` on the few write-read paths) is the
  safer, faster baseline for a read-heavy desktop app.
- Scenario / impact: minor per-query overhead and a correctness trap for the 3 non-opted files
  (identify and confirm they are genuinely read-only in a follow-up). Low because most queries opt
  in.
- Confidence: High (config fact); the 3 outliers need naming in a targeted pass.

## SQL-05 Per-connection array allocation in the pragma path

- Severity: Low
- Class: perf (micro)
- Location: `Context/SqliteConnectionInterceptor.cs` — `Pragmas()`.
- Evidence: `private string[] Pragmas() => [ "PRAGMA journal_mode=WAL;", ..., $"PRAGMA busy_timeout={...};", ... ];`
  — a new array (and the interpolated busy-timeout string) is built on **every** `ConnectionOpened`.
- Why it is a problem: the WAL/synchronous/foreign_keys strings are constant; only `busy_timeout`
  depends on options, which are fixed at startup. The array could be built once (cached) in the
  singleton. Connection opens are frequent in a pooled desktop app, so this is a small but repeated
  allocation on a hot path (PERFORMANCE.md weighting).
- Scenario / impact: negligible individually; recorded for the perf roll-up. Cache the array once.
- Confidence: High.

---

### Notes carried forward
- SQL-01 (encryption default) is confirmed against the Desktop composition root in **T6/T12**; if
  the Desktop host does not set `EncryptionEnabled && K2Approved`, SQL-01 is a shipped-default High.
- SQL-02 (retention) feeds the **T13 latent-bug timeline** (unbounded growth ordered by trigger
  horizon) and relates to the server-side outbox growth examined in **T7/T8**.
- Migration ordering 001–033 (with the double-underscore ordinals on 020/022/024–033) was spot-read
  and is monotonic by the `NNN` token; no out-of-order or duplicate ordinal found in the 35-file set.
