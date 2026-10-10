# Sandbox scripts — AI agent guide

Build/run **Animora** (Avalonia .NET 10 desktop app) in THIS sandbox.
Run from repo root. All scripts self-source `_env.sh` (sets SDK path, moves
scratch off the tiny tmpfs, caps memory). No manual env needed.

## Commands
| Need | Command | Notes |
|---|---|---|
| First-time setup | `scripts/sandbox/install-prereqs.sh` | SDK + xvfb + restore. Idempotent. |
| Env/health report | `scripts/sandbox/doctor.sh` | SDK? xvfb? RAM/disk. Never fails. |
| Quick compile | `scripts/sandbox/build.sh` | Debug, Desktop app only (fastest). |
| Whole solution | `scripts/sandbox/build.sh full` | Debug, all projects. |
| Release build | `scripts/sandbox/build.sh release` | |
| Restore only | `scripts/sandbox/build.sh restore` | |
| Run (smoke test) | `scripts/sandbox/run.sh` | builds, launches under xvfb ~6s. |
| Run longer | `scripts/sandbox/run.sh --seconds 10` | |
| Run, skip build | `scripts/sandbox/run.sh --no-build` | |
| Clean output | `scripts/sandbox/clean.sh` | bin/obj. `--all` also clears caches. |
| **Server+DB: install PostgreSQL** | `scripts/sandbox/install-postgres.sh` | local throwaway PG 17 under `.sandbox/pg`, test-tuned. `status\|stop\|restart\|nuke`. |
| **Server+DB: build only** | `scripts/sandbox/server-test.sh --build-only` | server/DB/"S" tree only — NO desktop. |
| **Server+DB: test on real DB** | `scripts/sandbox/server-test.sh --require-pg` | runs `Animora.Server.Tests` against a REAL PostgreSQL; `--require-pg` fails (not skips) if DB missing. `--all` adds Persistence+Sync. |

## Server / database (Track S) testing
The server track (`Animora.Server`, `Animora.Persistence.PostgreSql`,
`Animora.Server.Licensing`, `Animora.Web.Admin`) has PostgreSQL-gated tests
(`[PgFact]` via `PgFixture` → `ANIMORA_PG_TEST_CONNECTION`). `install-postgres.sh`
stands up a real local cluster and writes the connection string to
`.sandbox/pg/connection.env`; `server-test.sh` sources it so the gated tests hit a
real DB instead of skipping. These scripts **never build the desktop app** or its
dependent test projects (`Animora.Domain.Tests`, `Animora.Application.Tests`).

## Reading results
- Exit `0` = success. Nonzero = failure (real dotnet exit code).
- `run.sh` success = "SMOKE RUN OK" (app survived the timer, no startup crash).
  Failure prints the exception + stack trace inline.
- Every script tees full output to `.sandbox/logs/<name>.log`. On build failure
  the error lines are auto-grepped and printed. Crash logs survive the crash.

## Env constraints (already handled by `_env.sh` — don't fight them)
- **No SDK preinstalled** → installed to `$HOME/.dotnet` (no root).
- **~985 MiB RAM** → workstation GC + `-m:1` + no Roslyn server. Don't add
  parallel/server-GC flags or the build gets OOM-killed.
- **`/tmp` = 493 MiB tmpfs** → scratch/NuGet/temp forced to `.sandbox/` on disk.
  Never point TMPDIR back at `/tmp`.
- **No display** → GUI runs only via xvfb (that's what `run.sh` does).

## Typical flow
```
scripts/sandbox/install-prereqs.sh   # once
scripts/sandbox/build.sh             # after code edits
scripts/sandbox/run.sh               # verify no startup crash
```
