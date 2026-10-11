# GitHub Actions for Animora — build & test from the sandbox

This repo ships a GitHub Actions CI that an agent running in THIS low-memory
sandbox can drive on demand to build and test both the **app** (Avalonia
desktop) and the **server** (ASP.NET Core + PostgreSQL) tracks — without paying
the cost of a full .NET 10 compile locally.

Everything is driven through one helper: **`scripts/sandbox/gh-ci.sh`**
(a thin, safe wrapper over the already-authenticated `gh` CLI).

---

## One-time install (why the workflows aren't already in `.github/workflows/`)

The sandbox's GitHub token is a **GitHub App user token without the
`workflows` permission**, so it cannot push files under `.github/workflows/`.
To work around that cleanly, the workflow YAML is version-controlled under
**`scripts/sandbox/workflows/`** instead, and installed on demand:

```bash
scripts/sandbox/gh-ci.sh install
```

This copies `ci.yml` + `_dotnet-build-test.yml` into `.github/workflows/`,
commits, and pushes. **If the push is rejected** for the `workflows` permission,
`install` prints the exact fix. The repo owner does ONE of:

```bash
# A) grant the scope once, then re-install
gh auth refresh -s workflow
scripts/sandbox/gh-ci.sh install

# B) or commit the two files from a permitted session
cp scripts/sandbox/workflows/*.yml .github/workflows/
git add .github/workflows/*.yml && git commit -m "ci: add workflows" && git push
```

Once the workflows exist on GitHub (either way), every command below works and
no further install is needed.

> There is also a leftover unconfigured template at
> `.github/workflows/dotnet-desktop.yml` (a GitHub starter that was never filled
> in — it still says `your-solution-name`). The sandbox token can't delete it
> (same `workflows` permission), so remove it from a permitted session:
> `git rm .github/workflows/dotnet-desktop.yml && git commit -m "ci: drop unused template" && git push`.
> It is harmless if left — it only runs on push/PR and will error, but it does
> not affect the `ci.yml` pipeline you drive with `gh-ci.sh`.

---

## Why this exists

| Local sandbox | GitHub Actions |
|---|---|
| ~985 MiB RAM, workstation GC, `-m:1`, no Roslyn server | Full multi-core runners |
| Slow, OOM-prone on full-solution builds | Fast, parallel, free for this repo |
| No real PostgreSQL unless you install one | `postgres:17` service container in seconds |
| Only Linux | Linux **and** Windows runners (real desktop target) |

So: iterate small edits locally with `build.sh`/`test.sh`; offload the heavy /
cross-platform / real-DB work to CI with `gh-ci.sh`.

---

## The workflows

| File | Role |
|---|---|
| `scripts/sandbox/workflows/ci.yml` → installed to `.github/workflows/ci.yml` | Entry point. Runs on push/PR to `main` (+ `genspark_ai_developer`) and on **manual `workflow_dispatch`** with inputs. Plans the OS matrix + tracks, then calls the reusable unit for the `app` and/or `server` track, then a `summary`. |
| `scripts/sandbox/workflows/_dotnet-build-test.yml` → installed to `.github/workflows/_dotnet-build-test.yml` | Reusable "how to restore/build/test" unit. Knows the project sets per track, spins up PostgreSQL for the server track, and uploads `.trx` results + build logs as artifacts. |
| `global.json` | Pins the .NET SDK to the 10.0 feature band so `setup-dotnet` and the sandbox resolve the same toolchain. |

> The `scripts/sandbox/workflows/` copies are the source of truth; edit them
> there and re-run `gh-ci.sh install` to update the live `.github/workflows/`.

### Tracks

- **app** — builds `src/Animora.Desktop` (or the whole solution when
  `scope=all`) and runs the host-independent tests:
  `Animora.Domain.Tests`, `Animora.Application.Tests`, `Animora.Sync.Tests`.
  Runs on Linux by default; request `--os windows`/`--os both` to validate the
  real Windows target.
- **server** — builds `src/Animora.Server` (or the whole solution when
  `scope=all`) and runs `Animora.Server.Tests` + `Animora.Persistence.Tests`
  against a **real `postgres:17` service**. The workflow exports
  `ANIMORA_PG_TEST_CONNECTION` and `ANIMORA_REQUIRE_PG=1` so the `[PgFact]`
  tests actually execute (and FAIL rather than skip if the DB is missing).
  Always Linux.

### `workflow_dispatch` inputs (mirrored by `gh-ci.sh build` flags)

| Input | Values | Default | `gh-ci.sh` flag |
|---|---|---|---|
| `scope` | `all`, `app`, `server`, `build-only`, `restore` | `all` | `--scope` |
| `configuration` | `Debug`, `Release` | `Debug` | `--config` |
| `os` | `linux`, `windows`, `both` | `linux` | `--os` |
| `filter` | xUnit `FullyQualifiedName~<substr>` | _(none)_ | `--filter` |
| `with_postgres` | `true`/`false` | `true` | `--no-postgres` sets false |

`scope` meanings:
- `all` — build the **whole solution** + run every track's tests (fullest gate).
- `app` / `server` — only that track (build + its tests).
- `build-only` — compile everything, run **no** tests.
- `restore` — `dotnet restore` only (fast dependency/health check).

---

## The `gh-ci.sh` commands

```
scripts/sandbox/gh-ci.sh install        one-time: install workflows on GitHub
scripts/sandbox/gh-ci.sh build [OPTS]   trigger CI, wait + stream (default)
scripts/sandbox/gh-ci.sh watch  [ID]    watch a run live (latest if omitted)
scripts/sandbox/gh-ci.sh logs   [ID]    full logs of a run
scripts/sandbox/gh-ci.sh failed [ID]    ONLY the failing step logs
scripts/sandbox/gh-ci.sh results[ID]    download + summarise .trx results
scripts/sandbox/gh-ci.sh list           recent runs (id/status/branch)
scripts/sandbox/gh-ci.sh status [ID]    one-line status
scripts/sandbox/gh-ci.sh rerun  [ID]    re-run failed jobs
scripts/sandbox/gh-ci.sh cancel [ID]    cancel a run
scripts/sandbox/gh-ci.sh open   [ID]    print the run's web URL
```

`[ID]` is optional — omit it and the helper targets the **latest** CI run.

### Example scenarios an agent might need

```bash
# 1. Full confidence gate: whole solution + all tests, wait for pass/fail.
scripts/sandbox/gh-ci.sh build
echo "exit=$?"          # 0 = green, non-zero = CI failed

# 2. I only touched the desktop app — app track, Release, both OSes.
scripts/sandbox/gh-ci.sh build --scope app --config Release --os both

# 3. I touched the server/DB — server track on a real PostgreSQL.
scripts/sandbox/gh-ci.sh build --scope server

# 4. Reproduce one failing class quickly.
scripts/sandbox/gh-ci.sh build --scope app --filter Layering

# 5. Does it even compile? (no tests)
scripts/sandbox/gh-ci.sh build --scope build-only

# 6. Fire-and-forget, inspect later.
scripts/sandbox/gh-ci.sh build --no-wait
scripts/sandbox/gh-ci.sh watch            # live
scripts/sandbox/gh-ci.sh failed           # if red, read failing steps
scripts/sandbox/gh-ci.sh results          # download + summarise .trx

# 7. Run the server track WITHOUT PostgreSQL (gated tests will skip).
scripts/sandbox/gh-ci.sh build --scope server --no-postgres
```

### Reading results

- `gh-ci.sh build` (without `--no-wait`) streams the run and **exits with the
  run's status** — branch on `$?`.
- On failure it automatically prints `gh run view --log-failed` (only the
  failing steps) so you don't scroll the whole log.
- `gh-ci.sh results [ID]` downloads the `test-results-*` artifacts into
  `.sandbox/ci-results/<run-id>/` and prints, per test project:
  `total / passed / failed / skipped`, plus the first error line of each
  failing test. It exits non-zero if any test failed.
- Build logs (`build.binlog` + `build.txt`) are uploaded as
  `build-logs-<track>-<os>` artifacts; `gh run download <id> --pattern 'build-logs-*'`
  to open the binlog in the MSBuild Structured Log Viewer.

---

## Any branch, any scenario

`gh-ci.sh build` runs the workflow from your **current branch** by default
(`--ref <branch>` to override). Because the triggers include
`genspark_ai_developer`, pushing work to that branch also runs CI automatically;
manual `build` runs let you pick scope/config/OS/filter without pushing.

Tests added in the future are picked up automatically:
- New **app** tests belong in the `Animora.*.Tests` projects already wired into
  the app track; new **server** tests in `Animora.Server.Tests` /
  `Animora.Persistence.Tests`.
- With `scope=all` the whole solution is built and `dotnet test` discovers every
  test project, so brand-new test projects are covered the moment they are added
  to `Animora.sln`. (If you add a test project that must run in a narrower
  scope, list it in `_dotnet-build-test.yml` under the matching track's
  `APP_TESTS` / `SERVER_TESTS`.)

---

## Troubleshooting

- **`gh is not authenticated`** → `gh auth status`; re-login if needed. In this
  sandbox it's already logged in as `Arian-Alijani`.
- **No runs found** → trigger one first: `gh-ci.sh build`.
- **Server tests all skipped** → you passed `--no-postgres`, or ran a scope that
  excludes the server track. Use `--scope server` (PostgreSQL is on by default).
- **Windows service-container error** → the app track on `windows-latest` does
  not start PostgreSQL (by design); only the Linux server track does.
