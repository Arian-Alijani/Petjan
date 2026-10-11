#!/usr/bin/env bash
# =============================================================================
# ci-test.sh  —  scoped test run for Animora on a GitHub Actions runner
# =============================================================================
# Runs the test projects for the requested SCOPE and writes TRX result files
# (one per project) into ./TestResults so the workflow can upload them as an
# artifact and surface a pass/fail summary the model can read.
#
# Usage:
#   scripts/ci/ci-test.sh <scope> [configuration] [filter]
#     scope         = all | server | desktop | core
#                       all     -> every test project
#                       server  -> Server.Tests (+ Persistence + Sync)
#                       desktop -> Domain/Application/Persistence/Sync (no server)
#                       core    -> Domain + Application only (fast unit layer)
#     configuration = Debug | Release                    (default: Debug)
#     filter        = dotnet --filter FullyQualifiedName~<filter>   (optional)
#
# PostgreSQL gating (server scope):
#   The server tests include PostgreSQL-gated [PgFact] cases (PgFixture ->
#   ANIMORA_PG_TEST_CONNECTION). This script does NOT start a database; the
#   workflow decides. If ANIMORA_PG_TEST_CONNECTION is exported, those tests
#   run against the real DB; otherwise they skip (unless ANIMORA_REQUIRE_PG=1,
#   which turns a missing DB into a failure).
#
# Results:
#   * TRX files in ./TestResults/<project>.trx
#   * Markdown summary appended to $GITHUB_STEP_SUMMARY (if set).
#   * Nonzero exit if any project failed.
# =============================================================================

set -uo pipefail

SELF_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=_projects.sh
source "$SELF_DIR/_projects.sh"

SCOPE="${1:-all}"
CONFIG="${2:-Debug}"
FILTER="${3:-}"

if [ -t 1 ]; then C_RED=$'\033[31m'; C_GRN=$'\033[32m'; C_YLW=$'\033[33m'; C_BLU=$'\033[34m'; C_RST=$'\033[0m'
else C_RED=''; C_GRN=''; C_YLW=''; C_BLU=''; C_RST=''; fi
log()  { printf '%s[ci-test]%s %s\n' "$C_BLU" "$C_RST" "$*"; }
ok()   { printf '%s[  ok  ]%s %s\n' "$C_GRN" "$C_RST" "$*"; }
warn() { printf '%s[ warn ]%s %s\n' "$C_YLW" "$C_RST" "$*" >&2; }
err()  { printf '%s[ FAIL ]%s %s\n' "$C_RED" "$C_RST" "$*" >&2; }
summary() { [ -n "${GITHUB_STEP_SUMMARY:-}" ] && printf '%s\n' "$*" >>"$GITHUB_STEP_SUMMARY" || true; }

case "$CONFIG" in Debug|Release) ;; *) err "Unknown configuration: '$CONFIG'"; exit 2 ;; esac

# --- resolve which test projects this scope runs -----------------------------
PROJECTS=()
case "$SCOPE" in
  all)
    PROJECTS=(
      "tests/Animora.Domain.Tests/Animora.Domain.Tests.csproj"
      "tests/Animora.Application.Tests/Animora.Application.Tests.csproj"
      "tests/Animora.Persistence.Tests/Animora.Persistence.Tests.csproj"
      "tests/Animora.Sync.Tests/Animora.Sync.Tests.csproj"
      "tests/Animora.Server.Tests/Animora.Server.Tests.csproj"
    ) ;;
  server)  PROJECTS=("${SERVER_TEST_PROJECTS[@]}") ;;
  desktop) PROJECTS=("${DESKTOP_TEST_PROJECTS[@]}") ;;
  core)
    PROJECTS=(
      "tests/Animora.Domain.Tests/Animora.Domain.Tests.csproj"
      "tests/Animora.Application.Tests/Animora.Application.Tests.csproj"
    ) ;;
  *) err "Unknown scope: '$SCOPE' (expected all|server|desktop|core)"; exit 2 ;;
esac

RESULTS_DIR="$REPO_ROOT/TestResults"
mkdir -p "$RESULTS_DIR"

FILTER_OPT=()
[ -n "$FILTER" ] && { FILTER_OPT=(--filter "FullyQualifiedName~$FILTER"); log "filter=FullyQualifiedName~$FILTER"; }

# Report PG gating state so the log makes it obvious which path ran.
if [ "$SCOPE" = "server" ] || [ "$SCOPE" = "all" ]; then
  if [ -n "${ANIMORA_PG_TEST_CONNECTION:-}" ]; then
    ok "ANIMORA_PG_TEST_CONNECTION set -> PostgreSQL-gated tests will RUN."
  else
    warn "ANIMORA_PG_TEST_CONNECTION not set -> [PgFact] tests will SKIP (set ANIMORA_REQUIRE_PG=1 to fail instead)."
  fi
fi

log "scope=$SCOPE  configuration=$CONFIG  projects=${#PROJECTS[@]}"
summary "### 🧪 Tests — scope \`$SCOPE\` ($CONFIG)"
[ -n "$FILTER" ] && summary "_Filter:_ \`FullyQualifiedName~$FILTER\`"
summary ""
summary "| Test project | Result |"
summary "|---|---|"

RC=0
START=$(date +%s)
for p in "${PROJECTS[@]}"; do
  TARGET="$(abspath "$p")"
  name="$(basename "$TARGET" .csproj)"
  log "testing $name ..."

  # --logger trx produces a machine-readable result file per project.
  dotnet test "$TARGET" -c "$CONFIG" --nologo -v minimal \
    --logger "trx;LogFileName=$name.trx" \
    --results-directory "$RESULTS_DIR" \
    "${FILTER_OPT[@]}"
  rc=$?
  if [ "$rc" -eq 0 ]; then
    ok "$name"
    summary "| \`$name\` | ✅ passed |"
  else
    err "$name (exit $rc)"
    summary "| \`$name\` | ❌ failed (exit $rc) |"
    RC=$rc
  fi
done
ELAPSED=$(( $(date +%s) - START ))

summary ""
if [ "$RC" -eq 0 ]; then
  ok "TESTS OK ($SCOPE/$CONFIG) in ${ELAPSED}s"
  summary "**Result:** ✅ All selected tests passed in ${ELAPSED}s."
else
  err "TESTS FAILED ($SCOPE/$CONFIG, exit $RC) after ${ELAPSED}s"
  summary "**Result:** ❌ Some tests failed (exit $RC) after ${ELAPSED}s. See the TRX artifact for details."
fi
exit "$RC"
