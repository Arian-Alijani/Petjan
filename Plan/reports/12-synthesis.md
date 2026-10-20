# T13 — Cross-Cutting Synthesis & Performance Roll-Up

> Synthesis of the full-system audit (T2–T12). No new source reading: this rolls up every finding
> already recorded in `01-domain.md` … `11-desktop-crosslayer.md`, assigns stable cross-report IDs,
> and re-groups by the audit targets the plan emphasizes (perf #1/#4/#9, server↔client #6, latent #3),
> plus a severity-ranked master table and the top-10 highest-risk items.
>
> Scope recap: Animora = vendor-hosted (SaaS) multi-tenant veterinary clinic management + accounting;
> offline-first Avalonia/.NET 10 desktop + ASP.NET Core 10 central server, custom command/state sync.
> Finding IDs below are the originals from each report (`DOM-*`, `APP-*`, `CW-*`, `SQL-*`, `INF-*`,
> `SYN-*`, `AUTH-*`, `ISO-*`, `SAC-*`, `BAK-*`, `UI-*`). Baseline-verification entries (the per-report
> `*-0N` "sound" notes) are **not** defects and are excluded from the counts below.

---

## (a) Master table — all findings, severity-ranked

Totals: **8 Critical/High**, **18 Medium**, **15 Low** (excluding 11 baseline-verification notes).

### Critical / High

| ID | Title | Class | Report | Location (symbol) |
|----|-------|-------|--------|-------------------|
| APP-01 | Replay-guard only covers `ICommand<Unit>` — non-Unit journaled commands can re-post | bug (idempotency) | 02 | `Behaviors/CommandJournalBehavior.cs` |
| CW-01 | Wire-v2 `ExpectedServerVersion` validated server-side, never sent by client | inconsistency (srv↔client) | 03 | `Contracts.Wire/Commands/CommandEnvelope.cs` |
| SQL-01 | Local store unencrypted by default; PII-at-rest encryption opt-in & dormant | isolation / latent security | 04 | `Context/LocalStoreServiceCollectionExtensions.cs:51` |
| SQL-02 | No row retention/compaction on append-only growth tables | latent (unbounded growth) | 04 | `Maintenance/LocalStoreMaintenance.Prune` |
| INF-01 | Business-date queries bypass the clock + time-zone abstraction | latent (time/culture/TZ) | 05 | `Client.Services` business-date callers |
| SYN-01 | Provisional→server id map is volatile (lost on restart) | sync (design/latent) | 06 | `Sync.Custom/CommandIdRemapService.cs` |
| SYN-02 | Settled money state never reconciled into local provisional records | sync / inconsistency | 06 | `Sync.Custom/SettlementReconciler.ReconcileOneAsync` |
| SAC-01 | Void projected differently by live path vs rebuild (projection divergence) | bug / inconsistency | 09 | `Finance/FinanceProjectionStore.cs` (`InvoiceVoidedEvent`) |
| SAC-02 | Document-number format overflows past 999,999/year | latent (overflow/format) | 09 | `Application/Numbering/DocumentNumber.Format` |
| BAK-01 | Local backup omits the key needed to restore it | backup (completeness/DR) | 10 | `LocalStoreMaintenance.BackupAsync` |
| BAK-02 | Server backup whole-DB; no per-tenant isolation in artifact | backup / isolation | 10 | `Operator/DatabaseBackupService.CreateAsync` |

> (SAC-02 and SAC-03 restate DOM-02 at the sales/accounting level; see cross-refs in (c).)

### Medium

| ID | Title | Class | Report | Location |
|----|-------|-------|--------|----------|
| DOM-01 | Weighted-average cost corrupts after oversell drives qty ≤ 0 | latent | 01 | `Inventory/StockCalculator.WeightedAverageCost` |
| DOM-02 | Fixed-amount invoice discount truncates a fractional decimal | bug / inconsistency | 01 | `Sales/InvoiceTotals.Compute` |
| APP-02 | Ambient-clock fallback in the command processor | latent (time/clock) | 02 | `Commands/LocalCommandProcessor.cs:139` |
| CW-02 | Wire-v2 `RuleSetVersion` evaluated by server, never set by client | inconsistency (srv↔client) | 03 | `CommandEnvelope.RuleSetVersion` |
| CW-03 | `CommandTypes` count is 94, not the documented 86 | inconsistency (doc vs SoT) | 03 | `Contracts.Wire/Commands/CommandTypes.cs` |
| SQL-03 | Store passphrase embedded as SQL literal in `PRAGMA key` | bug (security-adjacent) | 04 | `Context/MultipleCiphersEncryptionProvider.Apply` |
| INF-02 | `TenantScopedCache` does not enforce its own tenant/user keying invariant | isolation (#12) | 05 | `Caching/TenantScopedCache.cs` |
| INF-03 | `DebounceBuffer` overflow throws instead of flushing | latent | 05 | `Visits/DebounceBuffer.Enqueue` |
| SYN-03 | Settlement/history can mask an earlier rejected prerequisite | sync (latent/UX) | 06 | settlement apply + Sales/History read models |
| SYN-04 | Full request-body buffering + batch abort on double-401 | sync (resilience/perf) | 06 | `Sync.Custom/BearerAuthHandler.SendAsync` |
| SYN-05 | Reconciler oldest-first ordering is per-page, not global | sync (ordering/latent) | 06 | `Sync.Custom/SettlementReconciler.ReconcileAsync` |
| AUTH-01 | Device auto-approval removes the manual admission gate | provisioning / isolation | 07 | `Auth/LoginService.LoginAsync` |
| AUTH-02 | Higher server/licensing phases authored but unexecuted | latent (unverified code) | 07 | `Handlers/ServerHandlerModuleS07..S10.cs`, `Server.Licensing/**` |
| ISO-01 | Keyless tenant-filter-free user lookup is the one cross-tenant read | isolation (seam) | 08 | `Identity/UserLookupContext.cs` |
| ISO-02 | "Tenant data ⇒ SyncableEntity" is not enforced | isolation (latent) | 08 | `Conventions/ServerModelConventions.ApplyTenancy` |
| SAC-03 | Fixed-amount invoice discount truncated and uncapped | bug | 09 | `Sales/InvoiceTotals.cs` |
| SAC-04 | Rebuild loads all invoice ids + full unbounded scans | perf / latent (growth) | 09 | `Finance/FinanceProjectionRebuilder.RebuildAsync` |
| SAC-05 | Two disagreeing recurring-expense "due" authorities | inconsistency (latent) | 09 | `Finance/RecurringExpense.RecurringExpenseSchedule` |
| SAC-06 | Re-clear of a recovered bounced cheque can double-post clearing | latent / bug | 09 | `Finance/Cheque.MarkCleared` |
| BAK-03 | No automatic/scheduled server backup, no PITR | backup (latent/RPO) | 10 | `AdminBackupAdapter.CreateAsync` |
| BAK-04 | Local backup cadence state is in-memory only | latent | 10 | `Diagnostics/MaintenanceScheduler.cs` |
| BAK-05 | Restore rebuilds stock snapshots but not finance projections | bug (consistency) | 10 | `LocalStoreMaintenance.RestoreAsync` |
| UI-01 | Singleton page ViewModels hold their data for the whole session | latent (memory growth) | 11 | `App.axaml.cs ConfigureServices` |
| UI-02 | Statistics publish relies on implicit UI-thread resumption | latent (thread affinity) | 11 | `Pages/Statistics/StatisticsSectionViewModel.LoadAsync` |

### Low

| ID | Title | Class | Report | Location |
|----|-------|-------|--------|----------|
| DOM-03 | Two divergent "is it due" definitions for recurring expenses | inconsistency | 01 | `Finance/RecurringExpense.IsDue` |
| DOM-04 | Totals sum raw `long` minor units without `checked` | latent | 01 | `Sales/InvoiceTotals.Compute` |
| DOM-05 | `Distinct().ToList()` in schedule expander violates no-LINQ hot-path rule | perf / inconsistency | 01 | `Clinical/TreatmentScheduleExpander.ExpandSpecific` |
| APP-03 | Retry exponent clamp vs retry budget mismatch | latent / dead-config | 02 | `Commands/RetryPolicy.NextDelay` |
| APP-04 | LINQ on the per-journaled-command prerequisite path | perf / convention | 02 | `Behaviors/CommandJournalBehavior.PrerequisiteAsync` |
| APP-05 | Behavior "Stage N" doc numbers drift from real order | inconsistency (doc vs code) | 02 | behavior XML docs vs `Dispatch/Dispatcher.Chain` |
| CW-04 | Error `Category` not carried on the wire | inconsistency | 03 | `Domain/Results/ErrorCode.cs` |
| CW-05 | Journaled-command envelope builders omit v2 precondition fields | latent / consistency | 03 | `Behaviors/CommandJournalBehavior.cs` + builders |
| SQL-04 | No global no-tracking default for read queries | perf | 04 | `Context/AnimoraLocalDbContext.cs` |
| SQL-05 | Per-connection array allocation in the pragma path | perf (micro) | 04 | `Context/SqliteConnectionInterceptor.Pragmas` |
| INF-04 | Finance outbox worker silently drops unregistered message types | latent (financial) | 05 | `Workers/FinanceProjectionWorker.LogAndDrop` |
| INF-05 | Singleton clock/TZ providers do sync DB work on caller thread on first use | perf | 05 | `Time/ClinicTimeZoneProvider.GetClinicTimeZone` |
| SYN-06 | Page staging cap (64) below max batch pages | perf | 06 | `Sync.Custom/HttpStateSyncEngine.cs` (`MaxStagedPages`) |
| AUTH-03 | RBAC store lookups per command on decision-cache miss | perf | 07 | `Authorization/{AuthzDecisionCache,MembershipResolver}.cs` |
| AUTH-04 | No access-token revocation list; only stamp rotation + expiry | latent (security/time) | 07 | `Auth/AccessTokenIssuer.cs`, `LoginService.RefreshAsync` |
| ISO-03 | Operator filter-bypass is a broad, flag-gated grant | isolation (operator) | 08 | `Tenancy/NoUnnamedIgnoreFiltersInterceptor.QueryCompilationStarting` |
| ISO-04 | Provisioning scope-isolation documented, not enforced | provisioning (latent) | 08 | `Identity/TenantProvisioner` |
| SAC-07 | Per-event tracking invoice fetch in the projection drain | perf (N+1 shape) | 09 | `Finance/FinanceProjectionStore.ApplyAsync` |
| BAK-06 | Prune orders by mtime, not by backup's own timestamp | latent | 10 | `DatabaseBackupService.List`/`Prune` |
| BAK-07 | Restore copy with `overwrite:false` can strand the live store | latent | 10 | `LocalStoreMaintenance.RestoreAsync` |
| UI-03 | Per-build global hooks re-registered on every sign-out | latent (handler accumulation) | 11 | `App.axaml.cs OnFrameworkInitializationCompleted` |
| UI-04 | View cache falls back to uncached rebuild when View is parented | perf | 11 | `Shell/ViewCache.GetOrCreate` |
| UI-05 | Composition-root comments describe real services as "mock" | inconsistency (maintainability) | 11 | `App.axaml.cs ConfigureServices` |

---

## (b) Performance roll-up (audit targets #1 / #4 / #9)

Every perf finding, grouped by whether the fix is **cheap** (local, low-risk) or **structural**
(touches a data-access or batching design). Estimated impact is relative to the hot path named.

### Cheap wins (local edit, low risk)

| ID | Opportunity | Estimated impact | Fix shape |
|----|-------------|------------------|-----------|
| SQL-04 | No global `AsNoTracking` default | Medium — change-tracker overhead on every read query | Set `QueryTrackingBehavior.NoTracking` default; opt-in tracking for writes |
| SQL-05 | Per-connection pragma array alloc | Low — micro-alloc per connection open | Hoist static readonly pragma array |
| DOM-05 | `Distinct().ToList()` in schedule expander | Low–Medium — LINQ alloc on an expansion hot path | Replace with pre-sized loop + set, per no-LINQ rule |
| APP-04 | LINQ on per-journaled-command prerequisite | Low — alloc per journaled command | Loop instead of LINQ |
| INF-05 | Clock/TZ provider sync DB work on caller thread (first use) | Low (one-time) but blocks first caller | Warm/async-init at startup |
| SYN-06 | Page staging cap (64) below max batch pages | Low–Medium — extra round-trips on large pulls | Align `MaxStagedPages` with max batch pages |
| UI-04 | View-cache uncached rebuild when parented | Medium — rebuilds a View on navigation | Reparent/detach instead of rebuild |
| AUTH-03 | RBAC store lookups per command on cache miss | Medium (server) — DB hit per authz miss | Widen/prewarm `AuthzDecisionCache` |

### Structural (data-access / batching redesign)

| ID | Opportunity | Estimated impact | Fix shape |
|----|-------------|------------------|-----------|
| SAC-04 | Rebuild loads all invoice ids + full unbounded scans | High — rebuild cost grows O(all history); worsens with data | Paginate/stream rebuild; bounded batches; checkpoint |
| SAC-07 | Per-event tracking invoice fetch in projection drain | Medium–High — N+1 on outbox drain | Batch-load invoices per drain page; no-tracking |
| SYN-04 | Full request-body buffering + batch abort on double-401 | Medium — memory + wasted work on auth bounce | Stream body; retry-after-refresh instead of abort |
| SQL-02 | Append-only tables grow unbounded (also perf) | High over time — query + storage cost climbs forever | Retention/compaction job (see latent timeline) |

**Weighting note (plan #9):** the two highest-value perf items are **SAC-04** and **SAC-07** — both on
the finance-projection path, both scale with accumulated history, both on a server-visible correctness
surface. SQL-02 is listed in both perf and latent because unbounded growth degrades query cost first,
then eventually breaks.

---

## (c) Server ↔ client inconsistency roll-up (audit target #6)

Drift between what the **client sends** and what the **server validates/consumes**, plus doc-vs-code
single-source-of-truth drift.

| ID | Inconsistency | Direction | Consequence |
|----|---------------|-----------|-------------|
| CW-01 | `ExpectedServerVersion` validated by server, never populated by client | client→server gap | Optimistic-concurrency precondition is dead; version conflicts not caught at intake |
| CW-02 | `RuleSetVersion` evaluated by server, never set by client | client→server gap | Server rule-set gating can't key on client rule version |
| CW-05 | Journaled-command envelope builders omit v2 precondition fields | client-side | Root cause feeding CW-01/CW-02 — builders never fill the v2 fields |
| CW-03 | `CommandTypes` = 94 live, docs say 86 | doc vs source-of-truth | Any doc-driven handler/coverage reasoning is off by 8 |
| CW-04 | Error `Category` not carried on the wire | server→client loss | Client can't branch on category; only the code survives transport |
| SYN-02 | Settled money state not reconciled back into local provisional records | server→client apply gap | Local shows provisional amounts after server settlement (money correctness) |
| SYN-03 | Settlement/history can mask an earlier rejected prerequisite | server→client apply gap | UI shows success though a prerequisite command was rejected |
| DOM-02 / SAC-03 | Fixed-amount discount truncation differs from decimal intent | domain↔contract | Same bug surfaces in totals (DOM) and accounting (SAC) layers |

**Theme:** the wire-v2 precondition plumbing (`ExpectedServerVersion`, `RuleSetVersion`) exists on both
ends but is never filled on the client (CW-05 is the origin). Fixing CW-05 closes CW-01 and CW-02 together.

---

## (d) Latent-bug timeline (audit target #3) — ordered by how soon each triggers

| Horizon | ID | Trigger | What breaks |
|---------|----|---------| ----------- |
| **Immediate on specific op** | APP-01 | Non-Unit journaled command replayed | Double-apply (idempotency hole) — fires on the first replay, not "over time" |
| | SYN-01 | Desktop restart between push and settlement | Provisional→server id map lost → remap/settlement mismatch |
| | BAK-05 | Any restore from backup | Finance projections stale after restore (stock rebuilt, finance not) |
| | BAK-07 | Restore while live store present | `overwrite:false` strands the live store — restore silently no-ops |
| **Within a clinic's first year** | SAC-06 | Bounced cheque recovered then re-cleared | Double-posted clearing entry |
| | DOM-01 | Inventory oversold to qty ≤ 0 | Weighted-average cost corrupts |
| | SAC-05 / DOM-03 | Recurring expense evaluated by the other "due" authority | Expense posted twice or skipped |
| | INF-03 | Debounce buffer overflow under burst | Throws instead of flushing → lost buffered writes |
| **Year boundary / scale** | SAC-02 | Document number exceeds 999,999 in a year | Number format overflow / collision |
| | DOM-04 | Minor-unit totals exceed `long` without `checked` | Silent overflow on very large totals |
| **Slow accumulation (months→years)** | SQL-02 | Append-only tables never compacted | Storage + query cost climbs forever → eventual failure |
| | SAC-04 | Finance-projection rebuild on grown history | Rebuild time grows unbounded |
| | UI-01 | Long desktop session, many pages visited | Singleton VMs retain data → working-set growth |
| | UI-03 | Repeated sign-out/sign-in | Global hooks re-registered → handler accumulation leak |
| | BAK-03 / BAK-06 | Time without scheduled backup / prune by mtime | RPO gap; wrong backups pruned |
| | AUTH-04 | Compromised token before expiry | No revocation list → compromise bounded only by expiry |
| **Config / environment drift** | INF-01 / APP-02 | Timezone/culture change, ambient clock | Business-date queries wrong across TZ/culture |
| | AUTH-02 | Higher server/licensing phases exercised | Unverified S07–S20 code paths may fault on first real use |

---

## (e) Top-10 highest-risk items

Ranked by severity × blast-radius × likelihood, weighting perf (#9), isolation (#12), money, and sync.

1. **SQL-01 — Local store unencrypted by default.** PII-at-rest encryption is opt-in and dormant; a
   stolen/copied clinic laptop exposes patient + owner PII. Isolation + compliance blast radius. (High)
2. **SYN-02 — Settled money never reconciled into local provisional records.** Local money state can
   diverge from server-authoritative settlement indefinitely — a correctness defect in the accounting
   core, visible to the clinic. (High)
3. **SAC-01 — Void projected differently by live path vs rebuild.** A rebuild can produce different
   finance facts than the live projection → the books disagree with themselves. (High)
4. **BAK-01 — Local backup omits the restore key.** A backup you cannot restore is not a backup; silent
   DR failure discovered only during an actual recovery. (High)
5. **APP-01 — Replay guard misses non-Unit journaled commands.** Idempotency hole → double-apply on
   replay; with sync retries this is reachable in normal operation. (High)
6. **SYN-01 — Provisional→server id map lost on restart.** Offline-first clients restart routinely;
   losing the remap corrupts settlement correlation. (High)
7. **SAC-02 — Document-number overflow past 999,999/year.** High-volume tenants hit a hard format wall;
   collision/format break on legal accounting documents. (High, dated trigger)
8. **BAK-02 — Server backup has no per-tenant isolation.** Whole-DB artifact mixes all tenants; any
   backup handoff/restore leaks across tenants. Isolation #12 blast radius. (High)
9. **SAC-04 — Finance-projection rebuild unbounded.** Top perf risk (#9): rebuild cost grows with all
   history; eventually the rebuild (and any recovery relying on it) becomes impractical. (Medium, scaling)
10. **ISO-01 — Keyless, filter-free user lookup.** The one deliberate cross-tenant read; correct today
    but the single weakest point in otherwise defence-in-depth isolation. (Medium, isolation)

**Fix-sequencing hint (not a remediation plan):** CW-05 unlocks CW-01+CW-02 in one change;
SAC-01/SAC-04/SAC-07 cluster on the finance-projection path and are best fixed together;
BAK-01/BAK-05/BAK-07 cluster on the restore path.

---

*End of synthesis. Counts exclude the 11 baseline-verification notes. All IDs trace back to their
originating report (`01`–`11`) listed in the Report column of the master table.*
