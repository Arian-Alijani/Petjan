#!/usr/bin/env bash
# =============================================================================
# run.sh  —  launch the Animora desktop app in this HEADLESS sandbox
# =============================================================================
# There is NO display in this sandbox, so the app is launched under Xvfb (a
# virtual framebuffer). This is a SMOKE RUN: it boots the real app, builds the
# full visual tree and renders one frame, which is exactly what surfaces
# startup crashes such as XAML-populate InvalidCastExceptions.
#
# Behaviour:
#   * Builds first (fast Debug) unless --no-build.
#   * Runs for N seconds (default 6) then stops the app.
#   * SUCCESS  = app was still alive when the timer fired (no early crash).
#   * FAILURE  = app exited on its own before the timer -> a real crash; its
#                stack trace is printed AND saved to the log.
#
# Usage:
#   scripts/sandbox/run.sh                 # build + 6s smoke run
#   scripts/sandbox/run.sh --seconds 10
#   scripts/sandbox/run.sh --no-build
#   scripts/sandbox/run.sh --release
# =============================================================================

source "$(dirname "${BASH_SOURCE[0]}")/_env.sh"
require_dotnet

SECONDS_RUN=6
DO_BUILD=1
CONFIG="Debug"
for ((i=1; i<=$#; i++)); do
  a="${!i}"
  case "$a" in
    --seconds) j=$((i+1)); SECONDS_RUN="${!j}"; ((i++)) ;;
    --no-build) DO_BUILD=0 ;;
    --release)  CONFIG="Release" ;;
    *) err "Unknown arg: $a"; exit 2 ;;
  esac
done

LOG="$LOG_DIR/run.log"
: >"$LOG"
exec > >(tee -a "$LOG") 2>&1
log "config=$CONFIG seconds=$SECONDS_RUN log=$LOG"

# 1. Build (unless skipped) ---------------------------------------------------
if [ "$DO_BUILD" = "1" ]; then
  MODE="fast"; [ "$CONFIG" = "Release" ] && MODE="release"
  log "Building before run ($MODE) ..."
  "$SCRIPT_DIR/build.sh" "$MODE" || { err "Build failed; not running."; exit 1; }
fi

DLL="$REPO_ROOT/$DESKTOP_DLL_REL/$CONFIG/net10.0/Animora.Desktop.dll"
[ -f "$DLL" ] || { err "Missing $DLL — build first."; exit 1; }

# 2. Need a display -----------------------------------------------------------
if [ "${ANIMORA_USE_EXISTING_DISPLAY:-0}" != "1" ] && ! command -v xvfb-run >/dev/null 2>&1; then
  err "xvfb not installed — cannot run a GUI app headlessly."
  err "Install it: scripts/sandbox/install-prereqs.sh   (or: sudo apt-get install -y xvfb)"
  exit 3
fi

# 3. Smoke-run under Xvfb -----------------------------------------------------
log "Launching under Xvfb for ${SECONDS_RUN}s ..."
set +e
if [ "${ANIMORA_USE_EXISTING_DISPLAY:-0}" = "1" ]; then
  timeout --preserve-status "${SECONDS_RUN}s" dotnet "$DLL"
else
  xvfb-run -a --server-args="-screen 0 1024x768x24" \
    timeout --preserve-status "${SECONDS_RUN}s" dotnet "$DLL"
fi
RC=$?
set -e

# timeout(1) exit convention:
#   124 = timed out  -> app was ALIVE the whole time = healthy startup.
#   With --preserve-status a SIGTERM'd child may report 143 (128+15). Also OK.
#   Anything else (esp. 134/SIGABRT, 139/SIGSEGV, 1) = the app crashed itself.
if [ "$RC" -eq 124 ] || [ "$RC" -eq 143 ]; then
  ok "SMOKE RUN OK — app stayed up for ${SECONDS_RUN}s with no startup crash."
  exit 0
else
  err "APP CRASHED at startup (exit $RC). Stack trace is above and in: $LOG"
  exit "$RC"
fi
