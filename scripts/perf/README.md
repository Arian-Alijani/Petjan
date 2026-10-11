# scripts/perf — performance & validation harnesses (G08)

Heavy lanes run on **GitHub Actions** (`perf-gates.yml`, authored at the repo root — copy to
`.github/workflows/`, see `scripts/ci/README.md`). The scripts here are also runnable locally on a
real machine; the Genspark sandbox must NOT run them (build/test/bench there is forbidden — drive
them via `scripts/ci/gh-ci.sh` / the workflow instead).

| Harness | Gate (ARCH §19) | Local entrypoint | CI job | Artifact |
|---|---|---|---|---|
| Offline budget (6 metrics, >2× = FAIL) | `O` (+ client perf) | `scripts/sandbox/bench.sh offline` (writes `backend-roadmap/measurements/C11-offline-budget.md`) | `offline-budget` | `artifacts/offline-budget.md` |
| Weak-server capacity (1–3 core / 2–4 GiB) | `T`, `I` | `scripts/perf/server-capacity.sh` | `server-capacity` | `artifacts/server-capacity.json` (+ `.md`) — *planning reference only* (A-RULE 035) |
| Zero-Internet | `Z` | `scripts/perf/zero-internet.sh` | `zero-internet` | stdout + `artifacts/zero-internet.txt` |
| Layering audit | R-B10 | `scripts/sandbox/test.sh Layering` | `layering-audit` | test log |
| Reconnect storm / mixed fleet | `I`, `M` | `scripts/sandbox/test.sh ReconnectStorm` / `MixedFleet` | `sync-behavioral` | test log |

## Running each harness

```bash
# 1) Offline budget — prebuilt Debug Desktop + Application.Tests, 60 s idle under Xvfb.
#    Exits non-zero ONLY on a >2x regression of any of the 6 budgets (thresholds are never edited).
scripts/sandbox/bench.sh offline
cp backend-roadmap/measurements/C11-offline-budget.md artifacts/offline-budget.md

# 2) Server capacity — needs Docker (cgroup-constrained container) + a PostgreSQL 18 admin connection.
export ANIMORA_PG_TEST_CONNECTION='Host=127.0.0.1;Port=5432;Username=animora_owner;Password=...;Database=postgres'
scripts/perf/server-capacity.sh            # --cpus=2 --memory=2g by default; override with CPUS= MEMORY=

# 3) Zero-Internet — static scan of a build/publish output, then a runtime smoke with egress blocked.
scripts/perf/zero-internet.sh [<published-dir>]   # default: src/Animora.Desktop/bin/Debug/net10.0
```

## Diagnostics

`scripts/sandbox/doctor.sh` reports PostgreSQL reachability and (since G07) whether the strict lane
(`ANIMORA_REQUIRE_PG=1`) is active — run it first if a PG-backed harness skips or fails to connect.

## Rules

- Capacity numbers are **recorded**, never a promised user count (A-RULE 035). The harness asserts
  only mechanical properties: no unexpected 5xx, no pool exhaustion, readiness green, bounded p99.
- Budgets in `scripts/sandbox/offline-budget.py` are not changed by this band.
