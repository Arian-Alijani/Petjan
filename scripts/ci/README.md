# Animora CI — GitHub Actions build & test system

This directory plus the workflow file **`animora-ci.yml`** (kept in the repo
root; see *Installation* below) give an AI agent / model a way to **build and
test Animora on GitHub's runners on demand, scoped to exactly what it needs, and
read the results back** — without building locally.

It is deliberately aligned with how the repo already builds in the sandbox
(`scripts/sandbox/*`) and on Windows (`scripts/windows/*`): the project
groupings, the PostgreSQL-gating contract, and the self-contained Windows
publish all mirror those scripts.

---

## Installation / updating (one manual step)

The agent cannot write under `.github/`, so the authoritative workflow is edited
in the repo root as **`animora-ci.yml`**, and a human copies it into place:

```bash
mkdir -p .github/workflows
cp animora-ci.yml .github/workflows/animora-ci.yml   # first time: git mv instead
git add .github/workflows/animora-ci.yml
git commit -m "ci: update Animora CI workflow (control-file lane)"
git push
```

> **This update matters.** The root `animora-ci.yml` now also triggers on pushes
> to `ci/**` and reads a `ci.params` control file, which is what makes
> `scripts/ci/gh-ci.sh` work from the Genspark sandbox (where
> `workflow_dispatch` is blocked). Until the `.github/workflows/` copy is
> refreshed from the root file, the sandbox lane won't run. The helper scripts
> in `scripts/ci/` are already in their final location and need no moving.

---

## What the model can ask for

Trigger **Actions → “Animora CI” → Run workflow** (or via `gh` — see below) and
choose:

| Input | Values | Meaning |
|---|---|---|
| `scope` | `all` · `server` · `desktop` · `core` | Which part of the product to build/test |
| `action` | `build-and-test` · `build-only` · `test-only` | Which stages to run |
| `configuration` | `Debug` · `Release` | Build configuration |
| `test_filter` | free text | Run only tests whose FQN contains this string |
| `server_tests_with_postgres` | `true`/`false` | Run server tests against a **real PostgreSQL** service, or let the `[PgFact]` tests skip |
| `publish_desktop` | `true`/`false` | Also produce a **downloadable self-contained Windows build** |
| `desktop_runtime` | `win-x64` · `win-arm64` | Runtime for the desktop publish |

### What each `scope` maps to

Defined once in [`_projects.sh`](./_projects.sh):

- **`server`** → `Animora.Server`, `Animora.Persistence.PostgreSql`,
  `Animora.Server.Licensing`, `Animora.Web.Admin`. Tests:
  `Animora.Server.Tests` (+ `Persistence` + `Sync`), PostgreSQL-gated.
  *(Runs on the Linux job — matches `scripts/sandbox/server-test.sh`.)*
- **`desktop`** → `Animora.Desktop` (the Avalonia Windows app). Tests:
  `Domain`, `Application`, `Persistence`, `Sync`.
  *(Runs on the Windows job — matches `scripts/windows/*`.)*
- **`core`** → the shared libraries (Domain, Application, Contracts,
  Persistence.Sqlite, Infrastructure.Local, Client.Services, Sync). Tests:
  `Domain` + `Application` (fast unit layer).
- **`all`** → everything: Linux builds/tests core+server; Windows
  builds/tests the desktop app. Combined.

---

## How the model reads the results

Every run produces, on its **Summary** page and as **Artifacts**:

1. **Step Summary (Markdown tables)** — a `📋 Plan` table (what was requested),
   per-project ✅/❌ build and test tables, a publish result, and a final
   `✅ CI result` roll-up. This is the fastest thing to read.
2. **Job logs** — full `dotnet build` / `dotnet test` output.
3. **`test-results-*` artifacts** — one `.trx` file per test project (machine
   readable; open to see individual failing cases).
4. **`animora-desktop-<runtime>` artifact** (only when `publish_desktop=true`) —
   the self-contained `Animora.Desktop.exe` + native libraries, downloadable and
   runnable on any Windows PC with no install.

Exit codes: any failing project fails its job; the `CI result` job is the single
required status (green only if every *needed* job succeeded — skipped jobs are OK).

---

## Driving CI from the Genspark sandbox  ⭐ (the way that actually works here)

**Important:** in the Genspark sandbox the authenticated GitHub token is a
**GitHub App token** that can *push commits* and *read* Actions, but is **not**
allowed to call the `workflow_dispatch` or re-run REST APIs. Both of these fail:

```text
$ gh workflow run animora-ci.yml -f scope=server ...
HTTP 403: Resource not accessible by integration
$ gh run rerun <id>
Resource not accessible by integration
```

So the model drives CI over the one channel that *is* allowed — **a git push** —
using a small control file (`ci.params`) and a dedicated `ci/**` branch. The
helper does everything in one command:

```bash
# Build + test EVERYTHING against a real PostgreSQL, wait, print the result:
scripts/ci/gh-ci.sh

# Only the server track, only tests, only the Sync.* cases:
scripts/ci/gh-ci.sh --scope server --action test-only --filter Sync

# Release build of the desktop app + a downloadable self-contained build:
scripts/ci/gh-ci.sh --scope desktop --action build-only --config Release \
    --publish true --runtime win-x64

# Fire-and-forget (don't block on the run); inspect later:
scripts/ci/gh-ci.sh --scope core --no-watch
gh run list --repo <owner>/<repo> --branch ci/sandbox
gh run view  --repo <owner>/<repo> --log-failed

# Keep the repo tidy — delete the scratch ci/ branch once the run is done:
scripts/ci/gh-ci.sh --scope core --cleanup
```

`gh-ci.sh` writes the knobs into `ci.params`, commits **only that file** onto a
scratch `ci/<name>` branch (default `ci/sandbox`, force-pushed each time),
pushes it, then watches the run and prints the Summary + any failing-job logs.
It never disturbs your checked-out branch or working tree. See
`scripts/ci/gh-ci.sh --help` for every flag. `ci.params.example` (repo root)
documents the file format if you ever want to write it by hand.

### What the model needs to remember
- **To launch a run:** call `scripts/ci/gh-ci.sh` with the flags you want.
- **It blocks until done** (unless `--no-watch`) and prints pass/fail + logs.
- **Each run is isolated** on its own `ci/**` branch — `main` and
  `genspark_ai_developer` are never touched by the model's experiments.
- **Any scenario, any time:** scope (all/server/desktop/core), stage
  (build/test/both), Debug/Release, a test-name filter, PostgreSQL on/off, and a
  downloadable Windows build — all selectable per run.

## Triggering with a classic PAT / OAuth token (outside the sandbox)

If you run with a token that has `actions:write` (a classic PAT, an OAuth
token, or from the Actions UI), the original manual dispatch still works:

```bash
# Build + test only the server track against a real PostgreSQL:
gh workflow run animora-ci.yml \
  -f scope=server -f action=build-and-test -f configuration=Debug \
  -f server_tests_with_postgres=true

# Build only the Windows desktop app in Release and publish a downloadable build:
gh workflow run animora-ci.yml \
  -f scope=desktop -f action=build-only -f configuration=Release \
  -f publish_desktop=true -f desktop_runtime=win-x64

# Run just the sync tests everywhere:
gh workflow run animora-ci.yml -f scope=all -f action=test-only -f test_filter=Sync

# Watch the latest run and read its result:
gh run watch
gh run view --log
```

On every push to `main` / `genspark_ai_developer` and every PR to `main`, the
workflow also runs automatically (`scope=all`, Debug, build + test) as a sanity
gate. Pushes to `ci/**` run exactly what their `ci.params` says.

---

## The scripts (also usable locally)

| Script | Purpose |
|---|---|
| [`_projects.sh`](./_projects.sh) | Single source of truth for which csproj is `server` / `desktop` / `core`. |
| [`ci-build.sh`](./ci-build.sh) | `ci-build.sh <scope> <config>` — scoped build, writes the build summary. |
| [`ci-test.sh`](./ci-test.sh) | `ci-test.sh <scope> <config> [filter]` — scoped tests, writes TRX + summary. Honors `ANIMORA_PG_TEST_CONNECTION` / `ANIMORA_REQUIRE_PG`. |
| [`ci-publish-desktop.sh`](./ci-publish-desktop.sh) | `ci-publish-desktop.sh <runtime> <out-dir>` — self-contained Windows publish (run on Windows). |
| [`gh-ci.sh`](./gh-ci.sh) | **Sandbox driver.** Writes `ci.params`, pushes it to a `ci/**` branch, watches the run, and prints the result + failing logs — the one command the model uses to launch CI here. |

They print color status to the terminal and, when `$GITHUB_STEP_SUMMARY` is set
(i.e. inside Actions), append the Markdown summaries described above. You can run
`ci-build.sh` / `ci-test.sh` in the sandbox too, though the dedicated
`scripts/sandbox/*` scripts are better there because they add the sandbox's
low-memory MSBuild clamps.

---

## Environment parity notes

- **.NET 10** is pinned (`DOTNET_VERSION: 10.0.x`) to match
  `Directory.Build.props` (`net10.0`).
- **PostgreSQL** uses the official `postgres:17` service container with the same
  `animora/animora` credentials the sandbox cluster uses, and the test step
  exports `ANIMORA_PG_TEST_CONNECTION` (and `ANIMORA_REQUIRE_PG=1`) exactly like
  `scripts/sandbox/server-test.sh` — so the `[PgFact]` tests run against a real
  DB instead of skipping.
- **Low-memory clamps are NOT applied** here on purpose: GitHub runners have
  plenty of RAM, unlike the ~985 MiB sandbox the `_env.sh` clamps exist for.
- **The Windows desktop publish** uses the same flags as
  `scripts/windows/publish-portable.ps1` (`--self-contained`,
  `PublishSingleFile`, `EnableAvaloniaXamlCompilation`, native SQLite left beside
  the exe).
