#!/usr/bin/env bash
# =============================================================================
# server-test.sh  —  build + test the SERVER / DATABASE (Track S) against a
#                    REAL PostgreSQL, in this sandbox, end to end.
# =============================================================================
# Scope (by user directive): the backend SERVER track only — Animora.Server,
# Animora.Persistence.PostgreSql, Animora.Server.Licensing, Animora.Web.Admin
# and their tests. The desktop app (Animora.Desktop) and its dependent test
# projects (Animora.Domain.Tests, Animora.Application.Tests) are NEVER built or
# run here — this script targets only the server/DB/"S" surface.
#
# What it does, in the fastest order:
#   1. Ensure the .NET SDK is present       (scripts/sandbox/install-prereqs.sh --no-xvfb)
#   2. Ensure a local PostgreSQL is running (scripts/sandbox/install-postgres.sh)
#      and export ANIMORA_PG_TEST_CONNECTION so the PgFixture tests hit a REAL DB.
#   3. Run the chosen server/DB test projects via `dotnet test` under the
#      sandbox's memory-safe MSBuild flags.
#
# PostgreSQL-gating:
#   PgFixture skips [PgFact] tests when ANIMORA_PG_TEST_CONNECTION is unset. This
#   script sets it, so the gated tests RUN against the real cluster. Pass
#   --require-pg to also set ANIMORA_REQUIRE_PG=1 (a missing DB then FAILS
#   instead of skipping) — use it to prove the DB path actually executed.
#
# Usage:
#   scripts/sandbox/server-test.sh                 # server tests, real PG
#   scripts/sandbox/server-test.sh --all           # + Persistence + Sync backend tests
#   scripts/sandbox/server-test.sh --require-pg     # fail (not skip) if PG missing
#   scripts/sandbox/server-test.sh --no-pg          # skip PG startup (gated tests skip)
#   scripts/sandbox/server-test.sh --filter NAME    # FullyQualifiedName~NAME
#   scripts/sandbox/server-test.sh --build-only     # restore+build the server tree, no tests
# =============================================================================

source "$(dirname "${BASH_SOURCE[0]}")/_env.sh"
require_dotnet

# --- the server/DB test projects this script owns (NO Desktop-dependent ones) -
SERVER_TEST_PROJ="$REPO_ROOT/tests/Animora.Server.Tests/Animora.Server.Tests.csproj"
PERSISTENCE_TEST_PROJ="$REPO_ROOT/tests/Animora.Persistence.Tests/Animora.Persistence.Tests.csproj"
SYNC_TEST_PROJ="$REPO_ROOT/tests/Animora.Sync.Tests/Animora.Sync.Tests.csproj"

WANT_ALL=0
WANT_PG=1
REQUIRE_PG=0
BUILD_ONLY=0
FILTER_ARG=""

while [ $# -gt 0 ]; do
  case "$1" in
    --all)        WANT_ALL=1 ;;
    --no-pg)      WANT_PG=0 ;;
    --require-pg) REQUIRE_PG=1 ;;
    --build-only) BUILD_ONLY=1 ;;
    --filter)     shift; FILTER_ARG="${1:-}"; [ -z "$FILTER_ARG" ] && { err "--filter needs a value"; exit 2; } ;;
    -h|--help)    sed -n '2,40p' "${BASH_SOURCE[0]}"; exit 0 ;;
    *)            err "Unknown arg: $1"; exit 2 ;;
  esac
  shift
done

LOG="$LOG_DIR/server-test.log"
: >"$LOG"
exec > >(tee -a "$LOG") 2>&1
log "server-test start  log=$LOG"

# -----------------------------------------------------------------------------
# 1. SDK (fast path: install-prereqs is idempotent and skips if present)
# -----------------------------------------------------------------------------
if ! has_dotnet; then
  log "No SDK — running install-prereqs.sh --no-xvfb (xvfb not needed for server tests) ..."
  "$SCRIPT_DIR/install-prereqs.sh" --no-xvfb
else
  ok "SDK present: $(dotnet --version)"
fi

# -----------------------------------------------------------------------------
# 2. PostgreSQL
# -----------------------------------------------------------------------------
if [ "$WANT_PG" = "1" ]; then
  log "Ensuring local PostgreSQL is up ..."
  "$SCRIPT_DIR/install-postgres.sh" install
  # install-postgres.sh writes the conn string here; source it into THIS shell.
  PG_CONN_FILE="$SCRATCH/pg/connection.env"
  if [ -f "$PG_CONN_FILE" ]; then
    # shellcheck disable=SC1090
    source "$PG_CONN_FILE"
    ok "ANIMORA_PG_TEST_CONNECTION set → real PostgreSQL will be used."
  else
    warn "connection.env missing — PG-gated tests will SKIP."
  fi
  [ "$REQUIRE_PG" = "1" ] && { export ANIMORA_REQUIRE_PG=1; log "ANIMORA_REQUIRE_PG=1 (gated tests FAIL if DB unreachable)"; }
else
  log "--no-pg: not starting PostgreSQL; PG-gated [PgFact] tests will SKIP."
fi

# -----------------------------------------------------------------------------
# 3. Collect the target projects
# -----------------------------------------------------------------------------
PROJECTS=("$SERVER_TEST_PROJ")
if [ "$WANT_ALL" = "1" ]; then
  PROJECTS+=("$PERSISTENCE_TEST_PROJ" "$SYNC_TEST_PROJ")
  log "Scope: server + persistence(SQLite) + sync backend tests."
else
  log "Scope: server tests only (pass --all to add Persistence + Sync)."
fi

FILTER_OPT=()
[ -n "$FILTER_ARG" ] && { FILTER_OPT=(--filter "FullyQualifiedName~$FILTER_ARG"); log "filter=FullyQualifiedName~$FILTER_ARG"; }

# -----------------------------------------------------------------------------
# 3a. build-only mode: restore + build each project, no test host
# -----------------------------------------------------------------------------
if [ "$BUILD_ONLY" = "1" ]; then
  RC=0
  for proj in "${PROJECTS[@]}"; do
    log "Building $(basename "$proj") ..."
    set +e
    dotnet build "$proj" -c Debug --nologo -v minimal "${MSBUILD_LOWMEM[@]}"
    rc=$?; set -e
    [ "$rc" -ne 0 ] && { RC=$rc; err "Build FAILED: $(basename "$proj") (exit $rc)"; }
  done
  [ "$RC" -eq 0 ] && ok "SERVER BUILD OK" || err "SERVER BUILD FAILED (exit $RC)"
  exit "$RC"
fi

# -----------------------------------------------------------------------------
# 3b. run the tests, one project at a time (lower peak memory than --sln)
# -----------------------------------------------------------------------------
RC=0
START=$(date +%s)
for proj in "${PROJECTS[@]}"; do
  log "Testing $(basename "$proj") ..."
  set +e
  dotnet test "$proj" -c Debug --nologo -v minimal \
    "${MSBUILD_LOWMEM[@]}" "${FILTER_OPT[@]}"
  rc=$?
  set -e
  [ "$rc" -ne 0 ] && { RC=$rc; err "FAILED: $(basename "$proj") (exit $rc)"; }
done
ELAPSED=$(( $(date +%s) - START ))

if [ "$RC" -eq 0 ]; then
  ok "SERVER TESTS OK (${ELAPSED}s)"
else
  err "SERVER TESTS FAILED (exit $RC) after ${ELAPSED}s — full log: $LOG"
  echo "----- failures -----"
  grep -nE "Failed |: error|error [A-Z]+[0-9]+|\[FAIL\]|Skipped " "$LOG" | head -60 || true
  echo "--------------------"
fi
exit "$RC"
