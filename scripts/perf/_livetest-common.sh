#!/usr/bin/env bash
# Shared helper for scripts/perf/live-*.sh — resolves the repo root, checks the .NET SDK and
# runs the Animora.LiveTest console against the LIVE server. Sourced, never executed directly.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$REPO_ROOT"

if ! command -v dotnet >/dev/null 2>&1; then
  echo "error: .NET 10 SDK not found on PATH (https://dot.net)." >&2
  exit 69
fi

# Credentials are never stored in the repo: they come from the operator's environment.
: "${ANIMORA_LT_USER:?set ANIMORA_LT_USER (manager account of the dedicated test tenant)}"
: "${ANIMORA_LT_PASSWORD:?set ANIMORA_LT_PASSWORD}"
export ANIMORA_LT_OUTPUT="${ANIMORA_LT_OUTPUT:-$REPO_ROOT/artifacts/live-test}"

livetest_run() {
  local mode="$1"
  echo ">> Animora.LiveTest '$mode' -> server ${ANIMORA_LT_SERVER_URL:-http://188.212.99.234:5000}, tenant ${ANIMORA_LT_TENANT:-ANIMORA-LOADTEST}"
  local rc=0
  dotnet run --project tests/Animora.LiveTest/Animora.LiveTest.csproj -c Release -- "$mode" || rc=$?
  echo ">> reports: $ANIMORA_LT_OUTPUT"
  ls -1t "$ANIMORA_LT_OUTPUT" 2>/dev/null | head -4 | sed "s#^#   $ANIMORA_LT_OUTPUT/#" || true
  return $rc
}
