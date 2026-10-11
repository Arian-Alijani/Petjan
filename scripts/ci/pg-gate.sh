#!/usr/bin/env bash
# =============================================================================
# pg-gate.sh — mandatory PostgreSQL correctness gate (G07, ARCH §19 lanes
# A/D/C/N/B/L/K/PG). Runs every PG-dependent suite with ANIMORA_REQUIRE_PG=1 so a
# missing database FAILS instead of skipping, then asserts ZERO skipped tests.
#
# Contract: the workflow exports ANIMORA_REQUIRE_PG=1 and
# ANIMORA_PG_TEST_CONNECTION before calling this script (never set them here).
#
#   scripts/ci/pg-gate.sh
# =============================================================================
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
SANDBOX_TEST="$REPO_ROOT/scripts/sandbox/test.sh"
LOG_DIR="${LOG_DIR:-$REPO_ROOT/.sandbox/logs}"
mkdir -p "$LOG_DIR"

if [ "${ANIMORA_REQUIRE_PG:-}" != "1" ]; then
  echo "✗ PG gate: ANIMORA_REQUIRE_PG=1 is required (strict lane) — refusing to run" >&2
  exit 2
fi
if [ -z "${ANIMORA_PG_TEST_CONNECTION:-}" ]; then
  echo "✗ PG gate: ANIMORA_PG_TEST_CONNECTION is not set" >&2
  exit 2
fi

# Filters cover every class in the G07 PG-gated set:
#   Server      -> Animora.Server.Tests (PgGatedTests, TenantIsolationTests, JournalOrderingTests,
#                  BatchFreezerTests/GcTests/SyncIsolationTests/SyncFlowTests, SnapshotAgeMonitorTests,
#                  QueryBudgetPgTests, RlsLeakTests, RestoreDrillTests, PgGateManifestTests)
#   Sync        -> Animora.Sync.Tests
#   Persistence -> Animora.Persistence.Tests (NativeSqliteVersionTests)
SUITES=(Server Sync Persistence)

TOTAL=0
SKIPPED=0
for suite in "${SUITES[@]}"; do
  echo "── PG gate: suite '$suite' ──"
  suite_log="$LOG_DIR/pg-gate-$suite.log"
  rc=0
  "$SANDBOX_TEST" "$suite" || rc=$?
  # test.sh always tees to test.log; keep one log per suite for the artifact.
  [ -f "$LOG_DIR/test.log" ] && cp -f "$LOG_DIR/test.log" "$suite_log"
  if [ "$rc" -ne 0 ]; then
    echo "✗ PG gate: suite '$suite' failed (exit $rc) — log: $suite_log" >&2
    exit "$rc"
  fi
  # dotnet summary lines: "Passed!  - Failed: 0, Passed: 12, Skipped: 0, Total: 12, ..."
  while IFS= read -r line; do
    p=$(sed -nE 's/.*Passed: ([0-9]+).*/\1/p' <<<"$line")
    s=$(sed -nE 's/.*Skipped: ([0-9]+).*/\1/p' <<<"$line")
    TOTAL=$(( TOTAL + ${p:-0} + ${s:-0} ))
    SKIPPED=$(( SKIPPED + ${s:-0} ))
  done < <(grep -E "(Passed|Failed)!.*Total:" "$suite_log" || true)
done

if [ "$SKIPPED" -gt 0 ]; then
  echo "✗ PG gate: $SKIPPED skipped (a PG test escaped the mandatory lane)" >&2
  exit 1
fi
echo "✓ PG gate: $TOTAL tests, 0 skipped"
