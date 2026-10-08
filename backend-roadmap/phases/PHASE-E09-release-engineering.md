# Phase E09 — Release engineering

**Band:** M · **Track:** E · **Depends on:** E08 · **Satisfies:** R-B27, R-B31, R-B33
**Runnable after this phase:** desktop offline yes · server required no (release tooling is scripts +
docs + packaging config; the one release-profile encryption-flag edit is the only runtime-path change — Zero-Internet holds)

## Read before starting
- `backend-roadmap/CONVENTIONS.md` §1, §2, §11, §12, §13
- `backend-roadmap/PROGRESS.md` (A01 entry — `WireContract.Version`, `Directory.Packages.props`
  central pins, lock-file mode, `NuGetAuditMode`; A11 entry — encryption provider seam,
  backup/restore, corrupt-DB quarantine/rebuild, native-SQLite runtime probe, `InstallationIdentity`,
  `ISecretStore`; D01 entry — Kestrel/HTTPS options, `SslProtocols.None`, Data Protection persisted
  key ring + stable application name, health/readiness endpoints; D02 entry — server migrations from
  `101`, `rowversion`; D08 entry — contract-version header + N/N-1/N-2 rejection,
  `WireContract.SupportedVersions` / `WireContract.MinSupportedVersion`, `ContractVersionMiddleware`;
  D10 entry — `Subscription`/`Seat`/license-issuance state; D11 entry — license signing
  key ring + `keyset.animora`, `license.animora`; D12 entry — file store bytes + reconciliation;
  D13 entry — local-only observability, `bench.sh`, capacity harness; E01 entry —
  `Animora.Web.Admin` hosted by `Animora.Server`, zero-CDN local assets; E07 entry — token/keystore,
  offline-session, device lifecycle; E08 entry — `gate-report.json`, `gates.sh`, unresolved
  `SKIPPED`/`FAIL` = release blocker, K2 residual CVE record)
- `STACK.md` §8 (Data Protection recovery), §11 (packaging: MSIX preferred / WiX fallback, macOS
  signed·notarized·stapled·offline·rollback), §12 (K2 native matrix freeze, SBOM + native inventory)
  · `ARCHITECTURE.md` §17 (deployment topology, §17.3 key/material recovery, §17.4 Zero-Internet),
  §18 (mixed-version N/N-1/N-2)
- `scripts/sandbox/**`, `Directory.Packages.props`, `Directory.Build.props`, `Animora.sln`

## Goal
Make Animora shippable and recoverable: a documented single-node deployment topology, an
install/upgrade/rollback runbook, a server backup/restore procedure covering DB + files + Data
Protection keys + license material **with a rehearsed recovery**, a mixed-fleet N/N-1/N-2
compatibility check wired to the D08 contract-version set, desktop packaging (MSIX preferred, WiX
fallback) that is signed and rollback-capable, a Zero-Internet isolation test, an SBOM plus native
binary inventory, and a final production-readiness checklist that consumes E08's gate report. This
phase produces runbooks, scripts and packaging config only — it adds nothing to any runtime path except the single release-profile `EncryptionEnabled`/`K2Approved` flag edit declared under "Encryption-at-rest release gate".

## Design decisions (pre-made — implement, do not re-derive)
- **One new folder `release/` at repo root** (a docs+scripts+config folder, not a project; not added
  to `Animora.sln`). It holds every deliverable of this phase so nothing leaks into `src/`.
- **Deployment topology (`release/TOPOLOGY.md`)** — single-node self-hosted per `ARCHITECTURE.md`
  §17 / AD-13: one `Animora.Server` process (Business API + Datasync `/tables/*` + workers +
  `Animora.Web.Admin`) on Kestrel behind the local reverse-proxy/TLS termination described in D01;
  SQL Server 2025 (Express = small tier) on the same or an adjacent host; server filesystem for D12
  bytes; desktops connect over LAN/VPN only. No cloud component, no CDN, no external IdP/telemetry
  (A-RULE 016). Scale-out (Redis backplane, second instance, stamps) is explicitly a future ADR
  (ROADMAP §5), not part of the launch topology.
- **Install / upgrade / rollback runbook (`release/RUNBOOK-server.md`, `release/RUNBOOK-desktop.md`)**
  — ordered, copy-pasteable steps. Server upgrade order: drain → snapshot (backup step below) →
  `dotnet ef database update` to the target migration (from `101`) → deploy new binaries → verify
  health/readiness → verify a gate smoke subset (E08 `gates.sh gates` critical subset). Rollback:
  stop → restore the pre-upgrade snapshot → redeploy N-1 binaries → verify. Migrations are additive
  and never edited (ROADMAP §3.5), so N-1 binaries run against the N schema for the rollback window;
  the runbook states the one-way migrations, if any, and their guard.
- **Backup / restore with a rehearsed recovery (`scripts/release/backup.sh`,
  `scripts/release/restore.sh`, `release/RECOVERY-REHEARSAL.md`)** — the backup set is the four
  co-equal parts required by `ARCHITECTURE.md` §17.3 / §10.6: (1) SQL Server DB (native backup),
  (2) D12 file-store bytes + their metadata rows, (3) the Data Protection persisted key ring (D01),
  (4) license material (`keyset.animora` signing set + issued `license.animora`). A backup missing
  any part is invalid. `restore.sh` restores all four and then runs a **rehearsal assertion**:
  server starts, health green, a previously-sealed payload decrypts (proves the key ring restored —
  E08 gate S), a signed license verifies (proves license material restored — gate U), and one file
  reconciles (gate T). `RECOVERY-REHEARSAL.md` records the last rehearsal run with its results;
  an un-rehearsed backup is treated as no backup.
- **Mixed-fleet N/N-1/N-2 check (`scripts/release/compat-check.sh`)** — drives E08's gate **M**
  against the D08 contract-version set: new client ↔ old server and old client ↔ new supported
  server across `WireContract.Version` N, N-1, N-2; asserts deterministic accept for supported
  versions and deterministic rejection (the D08 unsupported-version response) beyond N-2. The
  supported-version window is declared once in `release/COMPATIBILITY.md` and must match D08's
  `WireContract.SupportedVersions` / `WireContract.MinSupportedVersion` — the script fails if they
  diverge (R-B31).
- **Desktop packaging (`release/packaging/`)** — MSIX is the preferred Windows package (per
  `STACK.md` §11), WiX/MSI the supported fallback; both are configured, the build picks MSIX unless
  `PACKAGE_FALLBACK=wix`. The package is **signed** and **rollback-capable** (the installer keeps the
  prior version and supports downgrade-in-place); updates verify against the **same trust chain**.
  The macOS lane (config only, built on a mac host) is signed · notarized · stapled ·
  offline-installable · rollback-capable. Packaging embeds all assets locally (zero CDN — E01) and
  never contacts an external host during install (A-RULE 016). No auto-update phones home; updates
  are operator/MDM-driven (`ARCHITECTURE.md` §10.5).
- **Zero-Internet isolation test (`scripts/release/isolation-test.sh`)** — the `ARCHITECTURE.md`
  §17.4 method: with the packaged server + a packaged desktop on an isolated host, remove — one at a
  time — DNS, Internet route, external time sync, any revocation service, and the package feed, and
  assert the product still installs (from the offline package), starts, logs in offline, posts a
  command locally, and observes its own metrics locally (reuses E08 gate V). Any external dependency
  discovered here is a release blocker logged against its owner phase.
- **SBOM + native binary inventory (`scripts/release/sbom.sh`, `release/SBOM/`)** — per `STACK.md`
  §12: generate the NuGet SBOM with `NuGetAuditMode=all` + lock-file mode (already on from A01), and
  **separately** produce the native binary inventory (the SQLite/cipher natives per RID, their
  versions, and their CVE status) — the NuGet SBOM does **not** cover the native layer. The
  inventory records the K2-frozen native/RID matrix (`STACK.md` §3.3) and, where an encrypted native
  is still `< 3.50.2`, the **dated accepted-risk note** for CVE-2025-6965 that E08's K2 skip/record
  produced. A native binary with no inventory row fails the release.
- **Production-readiness checklist (`release/RELEASE-CHECKLIST.md`)** — a single gate the release
  consumes: build 0/0, `test.sh` green, E08 `gate-report.json` has **no unresolved `SKIPPED` and no
  `FAIL`**, backup/restore rehearsal recorded within the release window, compat-check green,
  isolation-test green, SBOM + native inventory complete with every accepted-risk note dated, and
  packaging signed. `scripts/release/release-check.sh` mechanizes it: it parses the gate report,
  runs compat/isolation/sbom, and exits non-zero if any item is unmet. It is the final go/no-go.
- **Encryption-at-rest release gate (owner of production enablement — closes Phase-Problem §7.2).**
  Today `LocalStoreOptions.EncryptionEnabled` and `K2Approved` both default to `false`, so an
  unmodified build stores PII / medical / financial data in plaintext and the `STACK.md` "encrypted at rest" stack decision (desktop offline store row) and `ARCHITECTURE.md` R3
  (secure, reliable, correct) is undelivered. **E09 owns closing that gap**, in two parts:
  1. **Gate (scripts only):** `release-check.sh` fails unless, **for every RID shipped**, E08's
     `Gate_K2_encrypted_lifecycle` is `PASS` (a `SKIPPED` K2 is a release blocker, not an accepted
     risk) **and** the native inventory shows the frozen cipher native for that RID.
  2. **Enablement — now performed in E07 (audit 2026-10-08 · SYNC-C-02, PH-01, supersedes the
     2026-10-07 decision below).** The actual `EncryptionEnabled = true` edit moves **forward to E07**
     (the first phase with a real server/credential path), so E03's `repl_*` and E05's
     `ConflictQueueEntries` PII surface is never shipped in plaintext through E03–E08. **E09 remains
     the release blocker/verifier**, not the enabler: `release-check.sh` fails if any shipped build
     resolves `EncryptionEnabled=false`, and the acceptance test still asserts the *release* profile
     resolves `EncryptionEnabled && K2Approved` and that `LocalStoreOptions.Validate()` accepts it.
     `K2Approved` is still set here once part 1 is green. No new package pin, no migration, no wire change.
  *(Superseded decision, kept for history — recorded 2026-10-07: "E09 rather than E07/E08, because E07
  is online identity and E08 is proof-only." The 2026-10-08 audit overrides this: the new replica/
  conflict PII surface makes plaintext-until-E09 unacceptable, so enablement is pulled to E07 while
  E09 keeps the release-gate veto.)*
- **No production code, no `src/` change (other than the single release-profile encryption edit above), no migration, no wire bump, no DI change, no new package
  pin** (packaging tools are invoked by scripts, not referenced by a project). `WireContract.Version`
  is **not** bumped here — E09 declares the supported window, it does not change the wire shape.

## Files to create / modify
| Path | Action | Contents (one line) |
|---|---|---|
| `release/TOPOLOGY.md` | create | single-node self-hosted topology + Zero-Internet stance |
| `release/RUNBOOK-server.md` | create | install/upgrade/rollback order, migration guard |
| `release/RUNBOOK-desktop.md` | create | desktop install/upgrade/rollback, trust-chain verify |
| `release/COMPATIBILITY.md` | create | declared N/N-1/N-2 supported window, matches D08 |
| `release/RECOVERY-REHEARSAL.md` | create | last backup/restore rehearsal record + result fields |
| `release/RELEASE-CHECKLIST.md` | create | the go/no-go checklist consuming E08's report |
| `release/packaging/msix/` | create | MSIX manifest + signing config (preferred) |
| `release/packaging/wix/` | create | WiX/MSI config (fallback) |
| `release/packaging/macos/` | create | signed·notarized·stapled·offline·rollback config |
| `release/SBOM/README.md` | create | SBOM + native-inventory format + accepted-risk note rule |
| `scripts/release/backup.sh` | create | backup DB + files + DP keys + license material |
| `scripts/release/restore.sh` | create | restore all four + rehearsal assertion (gates S/U/T) |
| `scripts/release/compat-check.sh` | create | drive gate M over N/N-1/N-2; fail on window divergence |
| `scripts/release/isolation-test.sh` | create | remove DNS/Internet/time/revocation/feed one at a time |
| `scripts/release/sbom.sh` | create | NuGet SBOM + native binary inventory |
| `scripts/release/release-check.sh` | create | mechanized RELEASE-CHECKLIST; final go/no-go |

## Implementation steps
1. Write `TOPOLOGY.md` from `ARCHITECTURE.md` §17 (single node, Zero-Internet, scale-out deferred).
2. Write the two runbooks with the ordered upgrade/rollback steps and the additive-migration guard.
3. Write `backup.sh` covering all four material classes; make a missing class fail the backup.
4. Write `restore.sh` with the rehearsal assertion (start → seal decrypt → license verify →
   reconcile), and record the run in `RECOVERY-REHEARSAL.md`.
5. Write `compat-check.sh` driving E08 gate M over the D08 version set; assert it matches
   `COMPATIBILITY.md`, fail on divergence.
6. Author `packaging/` (MSIX preferred, WiX fallback, macOS lane); ensure signed, rollback-capable,
   local-asset-only, no install-time external contact.
7. Write `isolation-test.sh` removing each external dependency one at a time (reuses gate V).
8. Write `sbom.sh` producing the NuGet SBOM and the separate native binary inventory with dated
   accepted-risk notes (K2 residual CVE).
9. Write `release-check.sh` parsing `gate-report.json` and running compat/isolation/sbom; non-zero on
   any unmet item.
10. Run the full release check on a build with no server present: it must correctly report the
    server-dependent items as blockers-until-run (not silently pass) while still producing the
    desktop package and desktop-scope reports.
11. `build.sh full` = 0/0; `test.sh` = `TESTS OK`; `run.sh` = `SMOKE RUN OK` with no server.

## Contracts / schema added
- No production contract, wire shape, entity, migration, DI registration, error code or package pin
  changes (the only `src/` touch is the release-profile `EncryptionEnabled`/`K2Approved` edit declared above). `WireContract.Version` is unchanged; E09 only *declares* the supported window.
- New `release/` folder (docs + packaging config) and `scripts/release/**` (backup, restore,
  compat-check, isolation-test, sbom, release-check). Artifacts: SBOM, native binary inventory,
  recovery-rehearsal record, release checklist result.

## Rules that apply here
- **R-B33** — deployment topology, install/upgrade/rollback runbook, rehearsed backup/restore,
  desktop packaging, and SBOM + native inventory all exist and are executed, not merely described.
- **R-B31 / A-RULE (§18)** — the N/N-1/N-2 window is declared once and enforced against D08's
  contract-version set; the compat check fails on any divergence.
- **R-B27 / A-RULE 016 / §17.4** — the isolation test removes every external dependency one at a
  time and the product keeps working; packaging contacts no external host and ships zero CDN assets.
- **ARCHITECTURE.md §10.6 / §17.3** — backup includes DP keys and license material; restore proves
  key-ring and license recovery (E08 gates S and U) before a backup counts as valid.
- **STACK.md §11** — MSIX preferred / WiX fallback; signed, rollback-capable; updates verify against
  the same trust chain; macOS signed·notarized·stapled·offline.
- **STACK.md §12** — the native binary inventory is separate from the NuGet SBOM and records the
  K2-frozen matrix plus the dated CVE-2025-6965 accepted-risk note where applicable.
- **CONVENTIONS §12 / §13** — E09 writes only under `release/` and `scripts/release/` plus the one
  declared release-profile encryption-flag edit (a decision recorded in this phase, not a re-litigation
  of A11/E08), introduces no forbidden dependency, and re-litigates no prior decision.

## Out of scope
- Enabling scale-out (Redis backplane, second instance, deployment stamps) → deferred (ROADMAP §5).
- Fixing a failing gate or an external dependency found by the isolation test → the owning phase from
  E08's `tracesTo`; E09 blocks the release, it does not patch product code.
- Enabling SQL RLS / trace flag 11042 → deferred; the RLS decision was recorded in E08.
- Any new runtime feature, screen, wire change or migration → none; this is the terminal phase.

## Acceptance criteria
- Build: `scripts/sandbox/build.sh full` = 0 warnings / 0 errors.
- `release/` and `scripts/release/**` exist with every file above; each runbook is ordered and
  copy-pasteable; `TOPOLOGY.md` states the single-node Zero-Internet topology.
- `scripts/release/backup.sh` produces a set with all four material classes; omitting any class
  fails the backup. `restore.sh` restores all four and its rehearsal assertion passes (server starts,
  a sealed payload decrypts, a signed license verifies, one file reconciles); the run is recorded in
  `RECOVERY-REHEARSAL.md`.
- `scripts/release/compat-check.sh` is green over N/N-1/N-2 and fails if `COMPATIBILITY.md` diverges
  from D08's version set.
- Packaging: MSIX (preferred) and WiX (fallback) configs build a signed, rollback-capable desktop
  package with local-only assets and no install-time external contact; the macOS lane is
  signed·notarized·stapled·offline·rollback-capable (config verified).
- `scripts/release/isolation-test.sh` is green: removing DNS, Internet, time sync, a revocation
  service, or the package feed — one at a time — leaves the product installable, startable, able to
  log in offline, post a local command, and observe local metrics.
- **Encryption gate:** `release-check.sh` fails when K2 is `SKIPPED`/`FAIL` for any shipped RID; the
  release profile resolves `EncryptionEnabled && K2Approved` and passes `LocalStoreOptions.Validate()`;
  dev/test profiles are unchanged.
- `scripts/release/sbom.sh` emits the NuGet SBOM and a separate native binary inventory; every native
  binary has a row, and any `< 3.50.2` encrypted native carries a dated CVE-2025-6965 accepted-risk
  note.
- `scripts/release/release-check.sh` is the final go/no-go: it fails if E08's `gate-report.json` has
  any unresolved `SKIPPED` or `FAIL`, if the rehearsal is stale, or if compat/isolation/SBOM are not
  green; it passes only when every checklist item is met.
- Runnability: `scripts/sandbox/run.sh` = `SMOKE RUN OK` with no server, no SQL Server, no network;
  E09 added nothing to any runtime path other than the declared release-profile encryption flags.

## PROGRESS.md update
Mark E09 done; advance "Next phase" to `— (roadmap complete)`; append a "Repo state" entry listing:
the `release/` folder (`TOPOLOGY.md`, both runbooks, `COMPATIBILITY.md`, `RECOVERY-REHEARSAL.md`,
`RELEASE-CHECKLIST.md`, `packaging/{msix,wix,macos}`, `SBOM/`), the `scripts/release/**` scripts
(`backup`, `restore` with the four-part set + rehearsal assertion, `compat-check` bound to D08's
version window, `isolation-test`, `sbom` with the separate native inventory + dated CVE note,
`release-check` as the go/no-go consuming E08's report), the declared N/N-1/N-2 window, the packaging
lanes (MSIX preferred / WiX fallback / macOS), and the confirmation that, apart from the release-profile `EncryptionEnabled`/`K2Approved` edit, no `src/`, migration, wire
version, DI or package-pin changed. Note that the roadmap is complete and the product is
release-gated by `release-check.sh`.
