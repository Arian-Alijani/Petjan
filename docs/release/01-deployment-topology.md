# 01 — Deployment topology & first install

> Operator runbook (single operator, single host). Normative source: `ARCHITECTURE.md` §3.2, §17.1.
> Companion runbooks: [`02-upgrade-rollback.md`](02-upgrade-rollback.md), [`03-backup-restore-hostmove.md`](03-backup-restore-hostmove.md).

## 1. Topology (one host)

```
 clients (desktop, offline-first) ──HTTPS──▶  reverse proxy / TLS (optional, same host)
                                                   │
                                  ┌────────────────▼─────────────────┐
                                  │  Animora.Server  (ONE ASP.NET Core process)
                                  │   • /health/live  /health/ready
                                  │   • Data-Protection key ring ──┐ (PostgreSQL, own context)
                                  └───────┬───────────────┬────────┘
                                          │               │
                                PostgreSQL 18      DataRootPath file tree
                                (server DB +       (<DataRootPath>/files/<TenantId>/…,
                                 key-ring tables)   content-addressed, §14)
```

Exactly four stateful things exist on the host, and nothing else is host-pinned:

| # | State | Where | Backed up by |
|---|---|---|---|
| 1 | Server database | PostgreSQL 18 database (e.g. `animora`) | runbook 03, part 1 |
| 2 | File-content tree | `Server__DataRootPath` (absolute path) | runbook 03, part 2 |
| 3 | Data-Protection key ring | tables `DataProtectionKeys` + `__EFMigrationsHistory_KeyRing` in the same PostgreSQL | runbook 03, part 3 |
| 4 | Configuration / secrets | environment or secret store (`Database__ConnectionString`, `Server__DataRootPath`, …) | operator's secret store |

The key ring is bound to the stable Data-Protection `ApplicationName` = `Animora.Server`
(`src/Animora.Server/Program.cs`). **Never change it** — doing so makes every issued token undecryptable.

The server needs **no Internet at runtime** (ARCH §17.4): licence verification is offline, identity is local,
observability is loopback-only, all web assets are packaged.

## 2. Roles & migration discipline (ARCH §17.1 — invariant)

| Role | Purpose | Rights |
|---|---|---|
| `animora_owner` (DDL/owner) | runs `dotnet-ef database update` **before traffic**; owns the schema | `CREATEDB`, DDL, **not** superuser |
| `animora_app` (runtime) | the role in `Database__ConnectionString` used by the running process | table DML + function `EXECUTE` only — **no DDL, no ownership, `NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE`** |

Rules:

1. **Migrations run before traffic, under the owner role.** Runtime startup only *checks* the schema version
   (`ServerSchemaVersion.ExpectedHead = "115_Files"`) and reports it through `/health/ready`; it never migrates.
2. `EnsureCreated` is **forbidden** outside test fixtures.
3. Server migrations are numbered from `101` (current head `115_Files`) and are **never edited** by a later
   change — new schema = a new, additive migration. The key-ring context has its own sequence (head `101_KeyRing`).
4. `scripts/ci/pg-init-roles.sh` is the executable reference for the two-role model (CI runs it on every gate).

## 3. First-install checklist

Prerequisites: Linux or Windows host (1–3 cores, 2–4 GiB RAM reference — a *planning reference only*, never a
guaranteed user count, ARCH §16.4), PostgreSQL 18, the published `Animora.Server` output, .NET 10 runtime
(or a self-contained publish), `dotnet-ef` 10.x on the **operator** machine (not needed on the runtime host
if you apply the idempotent SQL scripts instead).

- [ ] **1. PostgreSQL 18** installed on the host; apply a memory profile from `deploy/postgresql/`
      (`animora-2gib.conf` or `animora-4gib.conf`; keep `max_connections >= pool sum + 8`; durability settings stay on).
- [ ] **2. Provision roles** (as the PostgreSQL superuser):

```bash
psql -U postgres -d postgres <<'SQL'
CREATE ROLE animora_owner LOGIN PASSWORD :'OWNER_PW' NOSUPERUSER CREATEDB CREATEROLE;
CREATE ROLE animora_app   LOGIN PASSWORD :'APP_PW'   NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE;
CREATE DATABASE animora OWNER animora_owner;
SQL
```

  (`-v OWNER_PW=… -v APP_PW=…` on the `psql` line; never put passwords in shell history or JSON.)
- [ ] **3. Migrate as OWNER — before any traffic** (both contexts):

```bash
export ANIMORA_PG_DESIGN_CONNECTION='Host=127.0.0.1;Port=5432;Username=animora_owner;Password=…;Database=animora'
for ctx in AnimoraServerDbContext AnimoraKeyRingDbContext; do
  dotnet ef database update --context "$ctx" \
    --project src/Animora.Persistence.PostgreSql --startup-project src/Animora.Persistence.PostgreSql
done
```

- [ ] **4. Grant the runtime role DML only** (no DDL), then prove it:

```bash
psql -U animora_owner -d animora <<'SQL'
GRANT USAGE ON SCHEMA public TO animora_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO animora_app;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO animora_app;
GRANT EXECUTE ON ALL FUNCTIONS IN SCHEMA public TO animora_app;
SQL
# must FAIL (permission denied) — proof the runtime role has no DDL rights:
psql -U animora_app -d animora -c 'CREATE TABLE _ddl_probe(x int)' && echo "BAD: app role can DDL" || echo "ok: no DDL"
```

- [ ] **5. Create the file tree** (writable by the service account only; part of the backup set):

```bash
install -d -m 0750 -o animora -g animora /var/lib/animora          # Server__DataRootPath
```

- [ ] **6. Configure the process by environment / secret store** (never in JSON):

```bash
export Database__ConnectionString='Host=127.0.0.1;Port=5432;Username=animora_app;Password=…;Database=animora'
export Server__DataRootPath=/var/lib/animora
export ASPNETCORE_URLS='http://127.0.0.1:5000'      # behind the local reverse proxy / TLS terminator
export ASPNETCORE_ENVIRONMENT=Production
```

- [ ] **7. Start the single process; the key ring seeds itself** on first start (Data-Protection writes the
      first key into `DataProtectionKeys`). Confirm and **take the first backup immediately** (runbook 03) —
      a key ring that exists only on this host is the classic silent disaster.
- [ ] **8. Verify readiness:**

```bash
curl -fsS http://127.0.0.1:5000/health/live             # 200 = process up
curl -fsS http://127.0.0.1:5000/health/ready            # 200 = DB reachable, schema head ok, key ring ok, DataRoot writable
psql -U animora_app -d animora -tAc 'SELECT count(*) > 0 FROM "DataProtectionKeys"'   # t  = key ring seeded
```

  `/health/ready` = `503` names the failed probe (`database`, `schema`, `keyring`, `data-root`) (never its exception text): `schema` → step 3 was skipped
  or is behind; `keyring` → key-ring tables missing/unreachable (the server refuses readiness rather than
  mint ephemeral keys); `data-root` → step 5/6.
- [ ] **9. Record the baseline**: run the restore drill's equivalent on a scratch host once (runbook 03 §6) and
      store the measured RTO next to the host's runbook.

## 4. Release-gate prerequisite

Do not put a build into service unless the **launch gate** (`scripts/release/launch-gate.sh`, workflow
`release-gates.yml`) is green for that exact commit: it requires the PostgreSQL gates (`pg-gates.yml`),
the performance/zero-Internet gates (`perf-gates.yml`), the desktop smoke, and the SBOM + native-binary
inventory (`scripts/release/make-release-manifest.sh`). The generated `artifacts/launch-gate-report.md` is
the evidence of record.
