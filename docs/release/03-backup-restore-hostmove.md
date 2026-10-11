# 03 — Backup, restore & host move

> Operator runbook. Normative source: `ARCHITECTURE.md` §17.3 (A-RULE 038), §10.6, §16.2, §19 gates R + K.
> Topology: [`01-deployment-topology.md`](01-deployment-topology.md). Upgrades: [`02-upgrade-rollback.md`](02-upgrade-rollback.md).

Single-host is a **deliberate** choice with a **stated** recovery target. A host failure makes **all tenants
unavailable** until restore — backup gives disaster *recovery*, not *availability*. The numbers below are the
objectives this runbook must meet and the restore drill must prove.

## 1. Recovery objectives (named, never "best effort")

| Objective | Value | How it is met / proven |
|---|---|---|
| **RPO** (max data loss) | **≤ 5 minutes** with PITR; **≤ 24 h** is the floor if only the nightly logical dump exists | **Cadence:** continuous WAL archiving (`archive_mode=on`, `archive_timeout=60s`) + a base backup every night; the file tree is synced every 5 min (`rsync`/snapshot) and *paired* with the same PITR target (see §2). Fallback when PITR is not available: logical dumps (`pg_dump`) **every 6 h** → RPO ≤ 6 h — the operator must record which mode is live. |
| **RTO** (max time to readiness on a clean host) | **measured**, target **≤ 60 min** end-to-end including provisioning; the *software* part is measured by the drill | The G07 restore drill (`scripts/ci/restore-drill.sh`, CI job `restore-drill`) restores all three parts onto a clean database + directory, waits for `/health/ready` = 200 with a known tenant, and writes the elapsed seconds to **`artifacts/restore-rto.txt`**. Cite that number (and the run it came from) in the host's own record; re-measure on the real host once per quarter. RTO is *measured*, never asserted. |
| **Off-host durability** | **≥ 1 copy on different hardware from the live host** | A backup on the same disk/host/hypervisor as the data it protects **does not count**. Minimum: encrypted copy pushed to a second machine or object store in another failure domain after every base backup and continuously for WAL. |
| **Promotion / host move** | same restore + repoint config | §6. |
| **Scale-out trigger** | named in §7 | the §16.2 switch — when it fires, single-host is no longer acceptable. |

## 2. The backup set = THREE parts, ONE consistency boundary

All three are mandatory; a set missing any part is **not a backup**.

1. **PostgreSQL database** (server DB; logical dump or physical/PITR).
2. **File-content tree** — `Server__DataRootPath` (§14, content-addressed; a committed `FileId` is immutable).
3. **Data-Protection key ring** — tables `DataProtectionKeys` + `__EFMigrationsHistory_KeyRing`.
   **Forgetting this is the silent disaster: the DB restores but every token/entitlement blob is undecryptable.**
   The key ring is also inside the PostgreSQL base backup/PITR stream, but is **additionally dumped on its own**
   so it can be stored under separate, tighter access control and restored independently.

**The three parts are one point-in-time set, not three independent snapshots.** Capture them bracketed by a
write-freeze, *or* by an equivalent PITR-LSN + file-manifest pairing, so the DB point and the file snapshot
describe the same instant (no DB restored *behind* its files, no missing content). A restore proves
**file ↔ metadata parity**, not just that each part loaded.

### Backup procedure (copy-pasteable; logical-dump mode — PITR adds WAL shipping on top)

```bash
set -euo pipefail
TS=$(date -u +%Y%m%dT%H%M%SZ); OUT=/var/backups/animora/$TS; install -d -m 0700 "$OUT"
export PGHOST=127.0.0.1 PGUSER=animora_owner PGPASSWORD="$OWNER_PW" PGDATABASE=animora

# 0) freeze writes (stop the process, or put it behind the proxy's maintenance mode) — OR record the PITR LSN
systemctl stop animora-server                                   # write-freeze (restart at step 4)
psql -tAc 'SELECT pg_current_wal_lsn()' > "$OUT/pitr-lsn.txt"   # pairing marker for PITR mode

# 1) database, key ring excluded
pg_dump --no-owner --format=custom \
  --exclude-table='public."DataProtectionKeys"' --exclude-table='public."__EFMigrationsHistory_KeyRing"' \
  -f "$OUT/server.dump"
# 3) key ring on its own
pg_dump --no-owner --format=custom -t 'public."DataProtectionKeys"' -t 'public."__EFMigrationsHistory_KeyRing"' \
  -f "$OUT/keyring.dump"
# 2) file tree + manifest (parity check input)
tar -C /var/lib/animora -czf "$OUT/files.tar.gz" .
(cd /var/lib/animora && find . -type f -print0 | sort -z | xargs -0 sha256sum) > "$OUT/files.sha256"

# 4) unfreeze
systemctl start animora-server

# 5) integrity + OFF-HOST copy (mandatory — co-located does not count)
(cd "$OUT" && sha256sum server.dump keyring.dump files.tar.gz files.sha256 pitr-lsn.txt > SHA256SUMS)
rsync -a --partial "$OUT/" backup-host.example.net:/srv/animora-backups/$TS/    # different hardware
```

- [ ] Backup runs on the stated cadence (§1) and the last **off-host** copy is younger than the RPO.
- [ ] `SHA256SUMS` verifies on the off-host copy.
- [ ] The key-ring dump is stored with access limited to the operator (it can decrypt tokens).

## 3. Restore drill — rehearsed, not assumed (gate R, gate K)

A periodic drill restores all three parts onto a **clean** host and proves the server reaches readiness and
serves a known tenant. Automated in CI as the `restore-drill` job of `pg-gates.yml`
(`scripts/ci/restore-drill.sh`, in-process twin `RestoreDrillTests`):

- restore onto a clean database + directory → `/health/ready` = **200**, the known tenant row, the key and the
  file (hash-checked) are intact → elapsed seconds written to **`artifacts/restore-rto.txt`** (the measured RTO);
- **key-ring-missing variant**: restore DB + files but *not* the key ring → `/health/live` = 200 but
  `/health/ready` = **503** and business endpoints are refused (the server never mints ephemeral keys).

- [ ] Quarterly: run the equivalent on a scratch host with a **real** backup set; compare to `restore-rto.txt`;
      record both numbers.

## 4. Verifying a backup set (before you need it)

```bash
cd /var/backups/animora/<TS> && sha256sum -c SHA256SUMS
pg_restore --list server.dump  >/dev/null && pg_restore --list keyring.dump >/dev/null
tar -tzf files.tar.gz >/dev/null
```

## 5. Restore procedure (clean host or in-place disaster recovery)

```bash
set -euo pipefail
SET=/srv/animora-backups/<TS>                       # off-host copy brought onto the target host
(cd "$SET" && sha256sum -c SHA256SUMS)
export PGHOST=127.0.0.1 PGUSER=animora_owner PGPASSWORD="$OWNER_PW"

# 1) clean database
psql -d postgres -c 'DROP DATABASE IF EXISTS animora WITH (FORCE)' -c 'CREATE DATABASE animora OWNER animora_owner'
# 2) server data, then 3) the KEY RING (do not skip)
pg_restore --no-owner -d animora "$SET/server.dump"
pg_restore --no-owner -d animora "$SET/keyring.dump"
# 4) file tree + parity check
install -d -m 0750 -o animora -g animora /var/lib/animora && tar -C /var/lib/animora -xzf "$SET/files.tar.gz"
(cd /var/lib/animora && find . -type f -print0 | sort -z | xargs -0 sha256sum) | diff - "$SET/files.sha256" && echo "file parity OK"
# 5) re-grant runtime role (DML only), start, verify
psql -d animora -c 'GRANT SELECT,INSERT,UPDATE,DELETE ON ALL TABLES IN SCHEMA public TO animora_app; GRANT USAGE,SELECT ON ALL SEQUENCES IN SCHEMA public TO animora_app;'
systemctl start animora-server
curl -fsS http://127.0.0.1:5000/health/ready        # 200 = DB + schema + key ring + data root ok
```

- [ ] `/health/ready` = 200 and one known tenant signs in (proves the key ring restored with its data).
- [ ] Note the elapsed time → compare against RTO target (§1).
- [ ] If `/health/ready` = 503 with `keyring`: the key-ring part is missing/corrupt — restore it; **never** "fix" by
      deleting key-ring tables (that invalidates every issued token).
- [ ] PITR mode: restore the base backup, replay WAL to the recorded `pitr-lsn.txt` / timestamp, then restore the file
      snapshot taken at the same instant.

## 6. Host move / promotion

A host move **is** the restore in §5 plus repointing configuration — there is no host-pinned state beyond
`DataRootPath`, the database and the key ring, and the stable Data-Protection `ApplicationName` (`Animora.Server`)
means the **key ring remains valid** after the move.

- [ ] Provision the new host per runbook 01 (PostgreSQL 18, roles, `.NET` runtime) — **do not** run migrations by hand
      beyond what the restored schema head requires (the restored DB already carries it).
- [ ] Freeze writes on the old host; take a final backup set (§2); copy to the new host; restore (§5).
- [ ] Repoint configuration: `Database__ConnectionString`, `Server__DataRootPath`, reverse-proxy/DNS target. Keep
      `ApplicationName` unchanged.
- [ ] Verify `/health/ready` = 200; flip DNS/proxy; keep the old host stopped (never two writers).
- [ ] Desktop clients need nothing: they are offline-first and re-send queued commands idempotently by
      `(TenantId, OperationId)`.

## 7. Named scale-out trigger (§16.2)

Single-host stops being acceptable — and an **ADR** (A-RULE 031) must be written to move to the scale-out switch
(second web instance + Redis SignalR backplane; read replica / broker / OLAP remain separately-triggered) — when
**any** of these holds, measured by the G08 capacity harness (`artifacts/server-capacity.json`, *planning
reference only*) and production telemetry:

1. sustained p99 of the command-intake or state-pull profile exceeds the configured `statement_timeout` budget on
   the reference host after tuning, **or** peak PostgreSQL connections reach the bounded pool sum (12; readiness warns);
2. the restore drill's measured RTO (`artifacts/restore-rto.txt`) exceeds the contractual RTO, **or** the RPO cannot
   be met by the available backup cadence;
3. the commercial availability promise requires better than a single-host failure domain (i.e. "all tenants down
   until restore" is no longer acceptable to customers).

Until a trigger fires, the correct response to load is *tune and re-measure*, not re-architect.
