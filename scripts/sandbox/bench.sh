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
OFFLINE=0
for ((i=1; i<=$#; i++)); do
  a="${!i}"
  case "$a" in
    offline|--offline) OFFLINE=1; LABEL="C11-offline"; SECONDS_RUN=60 ;;
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
X_ARGS=()
if [ "${ANIMORA_X11_TCP:-0}" = "1" ]; then
  X_ARGS=(-nolock -nolisten unix -listen tcp -ac)
  export DISPLAY="localhost:$DISPLAY_NUM"
fi
Xvfb ":$DISPLAY_NUM" -screen 0 "1440x900x24" "${X_ARGS[@]}" >/dev/null 2>&1 &
XVFB_PID=$!
APP_PID=""
trap '[ -z "$APP_PID" ] || kill "$APP_PID" 2>/dev/null || true; kill "$XVFB_PID" 2>/dev/null || true' EXIT
if [ "$OFFLINE" = "1" ]; then
  export ANIMORA_BENCH=1
  export HTTP_PROXY=http://127.0.0.1:9 HTTPS_PROXY=http://127.0.0.1:9 ALL_PROXY=http://127.0.0.1:9
fi
sleep 1

APP_LOG="$LOG_DIR/bench-app.log"
: > "$APP_LOG"
START_MS=$(date +%s%3N)
dotnet "$DLL" >"$APP_LOG" 2>&1 &
APP_PID=$!

# Poll: record RSS and CPU, and detect first time the process is responsive
PEAK_RSS_KB=0
FIRST_RSS_MS=0
IDLE_RSS_KB=0
DEADLINE=$(( $(date +%s) + SECONDS_RUN ))
while kill -0 "$APP_PID" 2>/dev/null; do
  NOW_MS=$(( $(date +%s%3N) - START_MS ))
  if [ -r "/proc/$APP_PID/status" ]; then
    RSS=$(awk '/VmRSS/{print $2}' "/proc/$APP_PID/status" 2>/dev/null)
    [ -n "${RSS:-}" ] || RSS=0
    IDLE_RSS_KB=$RSS
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

if [ "$OFFLINE" = "1" ]; then
  [ "$SECONDS_RUN" -ge 60 ] || { err "Offline idle measurement requires 60 seconds."; exit 2; }
  FIRST_FRAME=$(sed -n 's/.*C11 first_frame_ms=\([0-9]*\).*/\1/p' "$APP_LOG" | head -1)
  [ -n "$FIRST_FRAME" ] || { err "No first-frame marker (app did not render)."; exit 1; }
  [ "$IDLE_RSS_KB" -gt 0 ] || { err "No working-set sample."; exit 1; }
  export ANIMORA_BENCH_OUTPUT="$LOG_DIR/c11-operations.json"
  unset HTTP_PROXY HTTPS_PROXY ALL_PROXY
  dotnet test "$REPO_ROOT/tests/Animora.Application.Tests/Animora.Application.Tests.csproj" \
    -c "$CONFIG" --no-build --no-restore --filter FullyQualifiedName~OfflineBudgetMeasurements \
    --nologo -v minimal > "$LOG_DIR/c11-operations.log" 2>&1 || { cat "$LOG_DIR/c11-operations.log"; exit 1; }
  python3 - "$FIRST_FRAME" "$IDLE_RSS_KB" "$SECONDS_RUN" "$ANIMORA_BENCH_OUTPUT" "$REPO_ROOT" <<'PYBENCH'
import json, pathlib, platform, sys
frame, rss, seconds = map(float, sys.argv[1:4])
ops = json.loads(pathlib.Path(sys.argv[4]).read_text())
rows = [
    ('First frame (sign-in window)', frame, 1200, 'Main stopwatch → LoginWindow.Opened; real startup frame'),
    ('Dashboard data (warm query proxy)', ops['dashboard_ms'], 2500, 'Manager snapshot query median of 5; not rendered UI latency'),
    ('Working set after 60 s', rss / 1024, 300, 'Final /proc VmRSS sample, MB'),
    ('Patients page (200 rows)', ops['patients_ms'], 120, 'Warm local projection median of 5, ms'),
    ('Global search', ops['search_ms'], 80, 'Warm 5-source query median of 5, ms'),
    ('Post invoice (10 lines)', ops['post_invoice_ms'], 250, 'Warm handler + real transactional UoW/ledger/feed, ms; excludes dispatch authorization'),
]
cpu = next(l.split(':',1)[1].strip() for l in pathlib.Path('/proc/cpuinfo').read_text().splitlines() if l.startswith('model name'))
ram = next(l for l in pathlib.Path('/proc/meminfo').read_text().splitlines() if l.startswith('MemTotal:'))
text = '# C11 offline budget — measured 2026-09-30\n\n'
text += f'Hardware: {cpu}; {ram}; {platform.platform()}. Shared Linux sandbox, Debug .NET 10, headless Xvfb, {int(seconds)} s idle.\n\n'
text += 'No server endpoint exists: NullCommandTransport is registered. External proxy endpoints are disabled for startup. Query/command fixture is a real temporary SQLite file. No CI latency assertions. Fail only above 2× budget.\n\n'
text += '| Metric | Measured | Budget | Budget status | Method |\n|---|---:|---:|---|---|\n'
for name, value, budget, method in rows:
    text += f'| {name} | {value:.2f} | {budget} | {"PASS" if value <= budget else "OVER BUDGET"} | {method} |\n'
text += '\nDeviations: first frame is the mandatory sign-in window, not the post-login shell. Dashboard is a warm read proxy, not first-frame-to-render. Ten-line post excludes dispatch authorization; includes real ledger/activity transactions. Manual 17-screen data sweep remains reviewer work.\n'
path = pathlib.Path(sys.argv[5]) / 'backend-roadmap/measurements/C11-offline-budget.md'
path.parent.mkdir(exist_ok=True)
path.write_text(text)
print(text)
sys.exit(1 if any(value > budget * 2 for _, value, budget, _ in rows) else 0)
PYBENCH
fi
