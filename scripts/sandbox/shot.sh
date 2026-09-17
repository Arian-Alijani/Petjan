#!/usr/bin/env bash
# =============================================================================
# shot.sh — launch Animora under Xvfb and capture a PNG screenshot
# =============================================================================
# Boots the real app in a virtual framebuffer, waits for it to render, then
# grabs the root window into a PNG so the UI can be visually verified in this
# headless sandbox. Output: .sandbox/shot.png (override with --out).
# =============================================================================

source "$(dirname "${BASH_SOURCE[0]}")/_env.sh"
require_dotnet

WAIT=7
OUT="$REPO_ROOT/.sandbox/shot.png"
CONFIG="Debug"
for ((i=1; i<=$#; i++)); do
  a="${!i}"
  case "$a" in
    --wait) j=$((i+1)); WAIT="${!j}"; ((i++)) ;;
    --out)  j=$((i+1)); OUT="${!j}";  ((i++)) ;;
    *) err "Unknown arg: $a"; exit 2 ;;
  esac
done

DLL="$REPO_ROOT/$DESKTOP_DLL_REL/$CONFIG/net10.0/Animora.Desktop.dll"
[ -f "$DLL" ] || { err "Missing $DLL — build first (scripts/sandbox/build.sh)."; exit 1; }

command -v Xvfb >/dev/null 2>&1 || { err "Xvfb not installed."; exit 3; }
command -v import >/dev/null 2>&1 || { err "ImageMagick 'import' not installed."; exit 3; }

DISPLAY_NUM=99
export DISPLAY=":$DISPLAY_NUM"
RES="1440x900x24"

log "Starting Xvfb on $DISPLAY ($RES) ..."
Xvfb "$DISPLAY" -screen 0 "$RES" >/dev/null 2>&1 &
XVFB_PID=$!
sleep 2

cleanup() {
  [ -n "${APP_PID:-}" ] && kill "$APP_PID" 2>/dev/null || true
  [ -n "${XVFB_PID:-}" ] && kill "$XVFB_PID" 2>/dev/null || true
}
trap cleanup EXIT

log "Launching app ..."
dotnet "$DLL" >"$LOG_DIR/shot-app.log" 2>&1 &
APP_PID=$!

log "Waiting ${WAIT}s for first render ..."
sleep "$WAIT"

if ! kill -0 "$APP_PID" 2>/dev/null; then
  err "App exited early — see $LOG_DIR/shot-app.log"
  tail -30 "$LOG_DIR/shot-app.log" >&2
  exit 1
fi

log "Capturing screenshot -> $OUT"
import -window root "$OUT"
ok "Screenshot saved: $OUT"
