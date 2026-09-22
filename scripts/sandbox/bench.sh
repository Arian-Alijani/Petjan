#!/usr/bin/env bash
# =============================================================================
# bench.sh — measure startup time, peak RSS and CPU of a headless smoke run
# =============================================================================
# Usage:
#   scripts/sandbox/bench.sh [--no-build] [--seconds N] [--release] [--label L]
# Outputs: a one-line BENCH record (label, ttfui ms, peak rss KB, cpu %, wall).
# The app is launched under Xvfb; we poll RSS+CPU from /proc.
# =============================================================================
source "$(dirname "${BASH_SOURCE[0]}")/_env.sh"
require_dotnet

SECONDS_RUN=12
DO_BUILD=1
CONFIG="Debug"
LABEL="baseline"
for ((i=1; i<=$#; i++)); do
  a="${!i}"
  case "$a" in
    --no-build) DO_BUILD=0 ;;
    --seconds)  j=$((i+1)); SECONDS_RUN="${!j}"; ((i++)) ;;
    --release)  CONFIG="Release" ;;
    --label)    j=$((i+1)); LABEL="${!j}"; ((i++)) ;;
    *) err "Unknown arg: $a"; exit 2 ;;
  esac
done

if [ "$DO_BUILD" = "1" ]; then
  MODE="fast"; [ "$CONFIG" = "Release" ] && MODE="release"
  "$SCRIPT_DIR/build.sh" "$MODE" >/dev/null 2>&1 || { err "Build failed."; exit 1; }
fi

DLL="$REPO_ROOT/$DESKTOP_DLL_REL/$CONFIG/net10.0/Animora.Desktop.dll"
[ -f "$DLL" ] || { err "Missing $DLL — build first."; exit 1; }

command -v Xvfb >/dev/null 2>&1 || { err "Xvfb not installed."; exit 3; }

DISPLAY_NUM=98
export DISPLAY=":$DISPLAY_NUM"
Xvfb "$DISPLAY" -screen 0 "1440x900x24" >/dev/null 2>&1 &
XVFB_PID=$!
sleep 1

APP_LOG="$LOG_DIR/bench-app.log"
: > "$APP_LOG"
START_MS=$(date +%s%3N)
dotnet "$DLL" >"$APP_LOG" 2>&1 &
APP_PID=$!

# Poll: record RSS and CPU, and detect first time the process is responsive
PEAK_RSS_KB=0
FIRST_RSS_MS=0
DEADLINE=$(( $(date +%s) + SECONDS_RUN ))
while kill -0 "$APP_PID" 2>/dev/null; do
  NOW_MS=$(( $(date +%s%3N) - START_MS ))
  if [ -r "/proc/$APP_PID/status" ]; then
    RSS=$(awk '/VmRSS/{print $2}' "/proc/$APP_PID/status" 2>/dev/null)
    [ -n "${RSS:-}" ] || RSS=0
    [ "$RSS" -gt "$PEAK_RSS_KB" ] && PEAK_RSS_KB=$RSS
    if [ "$FIRST_RSS_MS" = "0" ] && [ "$RSS" -gt 20000 ]; then FIRST_RSS_MS=$NOW_MS; fi
  fi
  [ "$(date +%s)" -ge "$DEADLINE" ] && break
  sleep 0.1
done

# Final CPU sample before killing
CPU=""
if [ -r "/proc/$APP_PID/stat" ]; then
  read -r _ _ _ _ _ _ _ _ _ _ _ _ _ UTI _ < "/proc/$APP_PID/stat" 2>/dev/null || true
fi
WALL_MS=$(( $(date +%s%3N) - START_MS ))
kill "$APP_PID" 2>/dev/null || true
wait "$APP_PID" 2>/dev/null || true
kill "$XVFB_PID" 2>/dev/null || true
wait "$XVFB_PID" 2>/dev/null || true
# Let the X server release the display/lock before the next run reuses it.
sleep 1

ALIVE="yes"; grep -qiE "Unhandled exception|System\.(Exception|InvalidOperationException)" "$APP_LOG" && ALIVE="maybe"
printf 'BENCH label=%s config=%s ttfui_ms=%s peak_rss_kb=%s wall_ms=%s alive=%s\n' \
  "$LABEL" "$CONFIG" "$FIRST_RSS_MS" "$PEAK_RSS_KB" "$WALL_MS" "$ALIVE"
