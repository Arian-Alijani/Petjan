# 02 — Upgrade & rollback

> Operator runbook. Normative source: `ARCHITECTURE.md` §17.2, §18, §8.6. Topology & roles: [`01-deployment-topology.md`](01-deployment-topology.md).
> **Always take a backup set first** ([`03-backup-restore-hostmove.md`](03-backup-restore-hostmove.md)).

## 1. Principles

- A release is the **same single process**; there is no second service to coordinate.
- Two supported deploy shapes: **stop → migrate → start**, or **blue/green** (new binary started on a second
  port against the already-migrated schema, then the proxy is flipped).
- **Migrations are additive** (new tables/columns/indexes, never edited, never destructive in the same release as
  the code that stops using the old shape). That is what makes a one-step-back **binary** rollback safe.
- **The wire contract is versioned.** `WireContract.IsSupported(version)` (`src/Animora.Contracts.Wire/WireContract.cs`)
  currently accepts `{1, 2}`; the server accepts **N / N-1 / N-2**. This is the rolling-upgrade **mixed-fleet
  guarantee**: desktops that have not updated yet keep working against the new server, and an unsupported
  version gets a controlled `426` + `X-Animora-Supported-Versions` (resync/upgrade), never a silent mis-map.
  (Proven by `MixedFleetTests`, gate M.)
- The runtime role has **no DDL rights**; the owner role migrates (runbook 01 §2).

## 2. Rollback boundary (state this before every upgrade)

| Change in the release | Binary rolls back one step without schema divergence? |
|---|---|
| Code only / additive migration (new nullable column, new table, new index) | **Yes** — previous binary ignores the new objects. |
| Wire contract bump (`WireContract.Version` N → N+1, additive DTOs) | **Yes** — N-1 clients are still accepted; N+1 clients get `426` from the rolled-back server and stay `Deferred` (never dropped) until the server is upgraded again. |
| Migration that **drops/renames/retypes** or backfills destructively | **No** — treat as a *forward-only* release: rollback = restore the pre-upgrade backup set (runbook 03). Such migrations must not ship in the same release as the code change; split them (expand → deploy → contract). |

**Rule of thumb:** a binary may roll back **at most one release (N → N-1)** over the schema, and only while
no *contract-phase* (destructive) migration has been applied. Beyond that boundary, rollback = restore.

## 3. Upgrade checklist — stop · migrate · start

- [ ] **0. Gate**: the launch gate is green for the exact commit (`artifacts/launch-gate-report.md`); the release
      manifest (SBOM + native inventory) is archived with the artifact.
- [ ] **1. Backup set** (DB + files + key ring as one point-in-time set) — verify it exists off-host.
- [ ] **2. Announce / drain**: `/health/ready` to the proxy; stop accepting new work (clients are offline-first and
      queue commands — a short outage is invisible to users).
- [ ] **3. Stop** the process.
- [ ] **4. Migrate as OWNER** (idempotent; safe to re-run):

```bash
export ANIMORA_PG_DESIGN_CONNECTION='Host=127.0.0.1;Port=5432;Username=animora_owner;Password=…;Database=animora'
for ctx in AnimoraServerDbContext AnimoraKeyRingDbContext; do
  dotnet ef database update --context "$ctx" \
    --project src/Animora.Persistence.PostgreSql --startup-project src/Animora.Persistence.PostgreSql
done
psql -U animora_owner -d animora -c 'GRANT SELECT,INSERT,UPDATE,DELETE ON ALL TABLES IN SCHEMA public TO animora_app; GRANT USAGE,SELECT ON ALL SEQUENCES IN SCHEMA public TO animora_app;'
```

- [ ] **5. Swap the binary** (keep the previous publish directory untouched as `…/previous`).
- [ ] **6. Start** and verify: `curl -fsS …/health/live`, then `…/health/ready` = `200` (schema head matches the new
      build's `ServerSchemaVersion.ExpectedHead`).
- [ ] **7. Smoke**: one real desktop sign-in + sync pull + one command round trip.
- [ ] **8. Keep the pre-upgrade backup set** until the next backup cycle has verified clean.

## 4. Upgrade checklist — blue/green

- [ ] Steps 0–1 as above. **Migrate first** (step 4) — the old (green) process keeps serving because the migration
      is additive.
- [ ] Start the new binary (blue) on a second port with the **same** `Database__ConnectionString` / `Server__DataRootPath`
      and the same `ApplicationName` (it is compiled in; do not override). Wait for `/health/ready` = `200` on blue.
- [ ] Flip the reverse proxy to blue; watch error rate and `/health/ready` for one sync interval.
- [ ] Stop green. **Never run two writers with different schema expectations** beyond the flip window.

## 5. Rollback checklist

**A. One-step-back binary rollback** (additive migrations only — see §2):

- [ ] Stop the process; start the previous publish directory (`…/previous`) with the same environment.
- [ ] `/health/ready` = `200`. Clients on N+1 receive `426` and stay deferred until re-upgrade; N and N-1 clients are unaffected.
- [ ] Do **not** run `dotnet ef database update <older>` — the additive schema stays; the old binary ignores it.

**B. Restore rollback** (a destructive/contract migration was applied, or data is corrupted):

- [ ] Stop the process; restore the **pre-upgrade backup set** — all three parts incl. the key ring (runbook 03 §5).
- [ ] Start the previous binary; `/health/ready` = `200`; accept the RPO loss since the backup point (clients re-send
      queued commands idempotently by `(TenantId, OperationId)` — nothing is applied twice).

## 6. Desktop clients

Desktop updates are distributed separately and verified against the same trust chain; the packaged output carries
its SBOM + native-binary inventory (`publish-portable.ps1 -EmitManifest`). Because the server tolerates N/N-1/N-2,
roll the server first, desktops afterwards — never the reverse across a wire-contract bump.
