#!/usr/bin/env bash
# =============================================================================
# test.sh  —  run the Animora test suite under this sandbox's memory limits
# =============================================================================
# Restores + builds the whole solution (same memory-safe configuration as
# build.sh: single MSBuild node, no shared Roslyn server, workstation GC) and
# runs every test project via `dotnet test Animora.sln`.
#
# Errors are ALWAYS visible: full output is tee'd to a log and the real dotnet
# exit code is preserved. Prints `TESTS OK` only when every test passed.
#
# Usage:
#   scripts/sandbox/test.sh                 # run all tests
#   scripts/sandbox/test.sh Domain          # only FQNs containing "Domain"
#   scripts/sandbox/test.sh Layering        # only the layering facts
# =============================================================================

source "$(dirname "${BASH_SOURCE[0]}")/_env.sh"
require_dotnet

FILTER_ARG=""
for a in "$@"; do
  case "$a" in
    -*) err "Unknown arg: $a"; exit 2 ;;
    *)  if [ -n "$FILTER_ARG" ]; then err "Only one filter arg allowed."; exit 2; fi
        FILTER_ARG="$a" ;;
  esac
done

LOG="$LOG_DIR/test.log"
: >"$LOG"
exec > >(tee -a "$LOG") 2>&1
log "solution=$(basename "$SOLUTION")  log=$LOG"

FILTER_OPT=()
if [ -n "$FILTER_ARG" ]; then
  FILTER_OPT=(--filter "FullyQualifiedName~$FILTER_ARG")
  log "filter=FullyQualifiedName~$FILTER_ARG"
fi

log "Running tests (Debug) ..."
START=$(date +%s)

set +e
dotnet test "$SOLUTION" -c Debug --nologo -v minimal \
  "${MSBUILD_LOWMEM[@]}" "${FILTER_OPT[@]}"
RC=$?
set -e

ELAPSED=$(( $(date +%s) - START ))

if [ "$RC" -eq 0 ]; then
  ok "TESTS OK (${ELAPSED}s)"
else
  err "TESTS FAILED (exit $RC) after ${ELAPSED}s — full log: $LOG"
  echo "----- failures -----"
  grep -nE "Failed |: error|error [A-Z]+[0-9]+|\[FAIL\]" "$LOG" | head -40 || true
  echo "--------------------"
fi
exit "$RC"
