#!/usr/bin/env bash
# =============================================================================
# build.sh  —  build Animora under this sandbox's memory limits
# =============================================================================
# Modes (pick what you need — trade speed vs coverage):
#   fast     (default) Debug build of ONLY the Desktop app + its refs.
#            Quickest edit/compile loop. Uses --no-restore when possible.
#   full     Debug build of the whole solution (all projects).
#   release  Release build of the whole solution.
#   restore  Just restore packages, no compile.
#
# Flags:
#   --clean-restore   force a fresh `dotnet restore` before building.
#
# Always memory-safe: single MSBuild node, no shared Roslyn server, workstation
# GC (see MSBUILD_LOWMEM in _env.sh) so it will not get OOM-killed at ~985 MiB.
#
# Errors are ALWAYS visible: full output is tee'd to a log, and the real dotnet
# exit code is preserved and printed even if the compile crashes.
#
# Usage:
#   scripts/sandbox/build.sh              # fast
#   scripts/sandbox/build.sh full
#   scripts/sandbox/build.sh release
#   scripts/sandbox/build.sh restore
#   scripts/sandbox/build.sh fast --clean-restore
# =============================================================================

source "$(dirname "${BASH_SOURCE[0]}")/_env.sh"
require_dotnet

MODE="fast"
CLEAN_RESTORE=0
for a in "$@"; do
  case "$a" in
    fast|full|release|restore) MODE="$a" ;;
    --clean-restore)           CLEAN_RESTORE=1 ;;
    *) err "Unknown arg: $a"; exit 2 ;;
  esac
done

LOG="$LOG_DIR/build-$MODE.log"
: >"$LOG"
exec > >(tee -a "$LOG") 2>&1
log "mode=$MODE  log=$LOG"

# Optional explicit restore ---------------------------------------------------
if [ "$CLEAN_RESTORE" = "1" ] || [ "$MODE" = "restore" ]; then
  log "Restoring packages ..."
  dotnet restore "$SOLUTION" -v minimal
  ok "Restore done."
  [ "$MODE" = "restore" ] && exit 0
fi

# Choose target + configuration ----------------------------------------------
case "$MODE" in
  fast)    TARGET="$DESKTOP_PROJ"; CONFIG="Debug"   ;;
  full)    TARGET="$SOLUTION";     CONFIG="Debug"   ;;
  release) TARGET="$SOLUTION";     CONFIG="Release" ;;
esac

# Reuse the warm cache: skip restore only when a previous restore is still on
# disk. After `clean.sh` the obj/ assets are gone, so --no-restore would fail
# with NETSDK1004 — detect that and let the build restore itself.
NORESTORE=(--no-restore)
if [ "$CLEAN_RESTORE" = "1" ] || \
   [ ! -f "$REPO_ROOT/src/Animora.Desktop/obj/project.assets.json" ]; then
  log "No restore assets found (or --clean-restore) -> restore enabled."
  NORESTORE=()
fi

log "Building $CONFIG :: $(basename "$TARGET") ..."
START=$(date +%s)

set +e
dotnet build "$TARGET" -c "$CONFIG" "${NORESTORE[@]}" \
  "${MSBUILD_LOWMEM[@]}" -v minimal
RC=$?
set -e

ELAPSED=$(( $(date +%s) - START ))

if [ "$RC" -eq 0 ]; then
  ok "BUILD OK ($CONFIG) in ${ELAPSED}s"
  DLL="$REPO_ROOT/$DESKTOP_DLL_REL/$CONFIG/net10.0/Animora.Desktop.dll"
  [ -f "$DLL" ] && log "Output: $DLL"
else
  err "BUILD FAILED (exit $RC) after ${ELAPSED}s — full log: $LOG"
  # Surface just the error/warning lines for a quick scan.
  echo "----- errors -----"
  grep -nE ": error|: warning|error [A-Z]+[0-9]+" "$LOG" | head -40 || true
  echo "------------------"
fi
exit "$RC"
