#!/usr/bin/env bash
# =============================================================================
# bench.sh — measure startup time, peak RSS and CPU of a headless smoke run
# =============================================================================
# Usage:
#   scripts/sandbox/bench.sh [--no-build] [--seconds N] [--release] [--label L]
#   scripts/sandbox/bench.sh offline   # prebuilt Desktop + Application.Tests, 60 s idle
# Outputs: a one-line BENCH record (label, ttfui ms, peak rss KB, cpu %, wall).
# The app is launched under Xvfb; we poll RSS+CPU from /proc.
# =============================================================================
source "$(dirname "${BASH_SOURCE[0]}")/_env.sh"
require_dotnet

SECONDS_RUN=12
DO_BUILD=1
CONFIG="Debug"
LABEL="baseline"
OFFLINE=0
for ((i=1; i<=$#; i++)); do
  a="${!i}"
  case "$a" in
    offline|--offline) OFFLINE=1; DO_BUILD=0; SECONDS_RUN=60; LABEL="offline" ;;
    --no-build) DO_BUILD=0 ;;
    --seconds)  j=$((i+1)); SECONDS_RUN="${!j}"; ((i++)) ;;
    --release)  CONFIG="Release" ;;
    --label)    j=$((i+1)); LABEL="${!j}"; ((i++)) ;;
    *) err "Unknown arg: $a"; exit 2 ;;
  esac
done

if [ "$OFFLINE" = "1" ]; then
  export ANIMORA_BENCH=1
  export ANIMORA_BENCH_OUTPUT="$LOG_DIR/c11-warm-metrics.json"
  rm -f "$ANIMORA_BENCH_OUTPUT"
  dotnet test "$REPO_ROOT/tests/Animora.Application.Tests/Animora.Application.Tests.csproj" \
    --no-build --no-restore --filter FullyQualifiedName~OfflineBudgetMeasurements -c "$CONFIG" \
    --nologo -v minimal > "$LOG_DIR/c11-budget-queries.log" 2>&1 || { cat "$LOG_DIR/c11-budget-queries.log"; exit 1; }
fi

if [ "$DO_BUILD" = "1" ]; then
  MODE="fast"; [ "$CONFIG" = "Release" ] && MODE="release"
  "$SCRIPT_DIR/build.sh" "$MODE" >/dev/null 2>&1 || { err "Build failed."; exit 1; }
fi

DLL="$REPO_ROOT/$DESKTOP_DLL_REL/$CONFIG/net10.0/Animora.Desktop.dll"
[ -f "$DLL" ] || { err "Missing $DLL — build first."; exit 1; }

command -v Xvfb >/dev/null 2>&1 || { err "Xvfb not installed."; exit 3; }

DISPLAY_NUM=98
export DISPLAY=":$DISPLAY_NUM"
Xvfb "$DISPLAY" -nolisten tcp -screen 0 "1440x900x24" >/dev/null 2>&1 &
XVFB_PID=$!
trap 'kill "${APP_PID:-}" "$XVFB_PID" 2>/dev/null || true' EXIT
sleep 1
kill -0 "$XVFB_PID" 2>/dev/null || { err "Xvfb failed"; exit 3; }

APP_LOG="$LOG_DIR/bench-app.log"
: > "$APP_LOG"
START_MS=$(date +%s%3N)
dotnet "$DLL" >"$APP_LOG" 2>&1 &
APP_PID=$!

# Poll: record RSS and CPU, and detect first time the process is responsive
PEAK_RSS_KB=0
LAST_RSS_KB=0
FIRST_RSS_MS=0
DEADLINE=$(( $(date +%s) + SECONDS_RUN ))
while kill -0 "$APP_PID" 2>/dev/null; do
  NOW_MS=$(( $(date +%s%3N) - START_MS ))
  if [ -r "/proc/$APP_PID/status" ]; then
    RSS=$(awk '/VmRSS/{print $2}' "/proc/$APP_PID/status" 2>/dev/null)
    [ -n "${RSS:-}" ] || RSS=0
    LAST_RSS_KB=$RSS
    [ "$RSS" -gt "$PEAK_RSS_KB" ] && PEAK_RSS_KB=$RSS
    if [ "$FIRST_RSS_MS" = "0" ] && [ "$RSS" -gt 20000 ]; then FIRST_RSS_MS=$NOW_MS; fi
  fi
  [ "$(date +%s)" -ge "$DEADLINE" ] && break
  sleep 0.1
done

ALIVE_AT_END=0
kill -0 "$APP_PID" 2>/dev/null && ALIVE_AT_END=1
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

if [ "$OFFLINE" = "1" ]; then
  [ "$SECONDS_RUN" -ge 60 ] || { err "Offline idle measurement requires 60 seconds"; exit 2; }
  [ "$ALIVE_AT_END" = "1" ] || { cat "$APP_LOG"; err "App exited before idle measurement"; exit 1; }
  mkdir -p "$REPO_ROOT/backend-roadmap/measurements"
  python3 "$SCRIPT_DIR/offline-budget.py" "$APP_LOG" "$ANIMORA_BENCH_OUTPUT" "$LAST_RSS_KB" \
    "$REPO_ROOT/backend-roadmap/measurements/C11-offline-budget.md"
fi
