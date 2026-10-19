# Animora.LiveTest

Operator-run console tool that drives the **live central server** (default `http://188.212.99.234:5000`) with
**real HTTP requests** through the same client stack the desktop ships (`Animora.Sync.Custom` transport +
`Animora.Application` commands). It answers two questions:

1. **Correctness / sync** — does every feature create, edit, converge between two devices, resolve conflicts and
   reject invalid input exactly as designed? (36 scenarios)
2. **Capacity** — how many concurrent clients can this server hardware carry, and which resource (CPU / RAM /
   PostgreSQL connections) runs out first?

It is **not** a unit test and never runs in CI gates; CI only *builds* it (scope `core`).

## Safety rules

- Use a **dedicated test tenant** only (e.g. `ANIMORA-LOADTEST`, licensed with `scripts/grant-test-subscription.sh`).
  Capacity mode refuses a tenant code that does not contain `TEST`, `LOAD` or `CAP`
  (override: `ANIMORA_LT_ALLOW_ANY_TENANT=1` — do not).
- Everything created is prefixed `lt-` (correctness: `lt-<run>-`, capacity: `lt-cap<run>-`). Correctness mode
  deletes what the domain allows; capacity rows stay (most aggregates have no delete verb) — reset the test tenant occasionally.
- Load is **ramped**, a profile **stops at its first unhealthy level**, and 503/429 back-pressure is retried and
  counted as *busy*, not as failure.
- The SSH monitor is **read-only** (reads `/proc`, `ss`, `systemctl`, `ps`, `pg_stat_activity`). It never writes on the server.
- Devices: the licence caps devices per tenant (seats × 2). Sessions are reused across runs (stable installation hash).
  Concurrency above `ANIMORA_LT_MAX_DEVICES` is produced by several virtual users sharing a device session.

## Prerequisites

- .NET 10 SDK, network access to the server.
- A manager account on the test tenant (not forced to change password — sign in once with the desktop app first).
- Optional, for hardware metrics: an SSH key for a user who can `sudo -n -u postgres psql` (otherwise PG
  connection counts show `n/a`; CPU/RAM/sockets/service still work) and the system `ssh` binary.

## Run

```bash
export ANIMORA_LT_USER=manager
export ANIMORA_LT_PASSWORD='…'
# optional: export ANIMORA_LT_TENANT=ANIMORA-LOADTEST ANIMORA_LT_SERVER_URL=http://188.212.99.234:5000

scripts/perf/live-correctness.sh          # preflight + correctness
ANIMORA_LT_SSH=1 ANIMORA_LT_SSH_USER=deploy ANIMORA_LT_SSH_KEY=~/.ssh/id_ed25519 \
  scripts/perf/live-capacity.sh           # preflight + capacity with hardware monitor
```

Or directly: `dotnet run --project tests/Animora.LiveTest -c Release -- preflight|correctness|capacity|all`
(`all` = correctness, then capacity only if correctness passed).

## Environment variables

| Variable | Default | Meaning |
|---|---|---|
| `ANIMORA_LT_SERVER_URL` | `http://188.212.99.234:5000` | Server base URL |
| `ANIMORA_LT_TENANT` | `ANIMORA-LOADTEST` | Test tenant code |
| `ANIMORA_LT_USER` / `ANIMORA_LT_PASSWORD` | — | Manager credentials (required) |
| `ANIMORA_LT_OUTPUT` | `artifacts/live-test` | Report directory |
| `ANIMORA_LT_FILTER` | — | Correctness: run scenarios whose `Feature/Name` contains this text |
| `ANIMORA_LT_KEEP_GOING` | off | Correctness: continue after a failure |
| `ANIMORA_LT_LEVELS` | `1,2,4,8,16,32` | Capacity: virtual-user ramp |
| `ANIMORA_LT_REQUESTS` | `60` | Capacity: requests per level (at least 2 × users) |
| `ANIMORA_LT_PROFILES` | all | Capacity: profile name filter (`reception-day`, `sales-rush`, `inventory-receipt`, `sync-pull`) |
| `ANIMORA_LT_MAX_DEVICES` | `8` | Capacity: max device sessions |
| `ANIMORA_LT_P95_LIMIT_MS` | `2000` | Capacity: p95 above this = unhealthy level |
| `ANIMORA_LT_SSH` | off | `1` enables the SSH hardware monitor |
| `ANIMORA_LT_SSH_HOST` / `_PORT` / `_USER` / `_KEY` | server host / 22 / — / — | SSH target |

A `livetest.json` next to the working directory may set the same fields (env wins).

## Reading the reports

- `correctness-<run>.md` — one PASS/FAIL/SKIP row per scenario. FAIL/ERROR carries the settlement outcome, error
  code and the steps before the failure. **SKIP is not a pass**: it means a tenant precondition is missing (for
  example no active doctor for scheduling).
- `capacity-<run>.md` — per profile and level: throughput, p50/p95/p99, failures, busy answers, server CPU peak,
  minimum free RAM, PostgreSQL connections, `/health/ready` result. The **Verdict** section gives the
  *recommended maximum concurrent virtual users*, the *limiter* (`cpu`, `memory`, `postgres-connections`,
  `software-or-disk`) and an *upgrade* hint. The JSON next to it holds the per-second hardware time series.
- Capacity numbers are a **planning reference only** (A-RULE 035), never a guarantee.

## Exit codes

`0` pass · `1` failed scenario / no healthy level · `64` bad mode or filter · `78` missing/invalid configuration.
