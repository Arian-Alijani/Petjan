#!/usr/bin/env bash
# Launch Animora under Xvfb, optionally click sidebar items, then screenshot.
# Usage: shot-page.sh OUT.png [click-x:click-y ...]
set -uo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/_env.sh"

OUT="${1:-$REPO_ROOT/.sandbox/shot.png}"
shift || true
WAIT=9
DLL="$REPO_ROOT/$DESKTOP_DLL_REL/Debug/net10.0/Animora.Desktop.dll"

DISPLAY_NUM=99
export DISPLAY=":$DISPLAY_NUM"
Xvfb "$DISPLAY" -screen 0 "1600x1000x24" >/dev/null 2>&1 &
XVFB_PID=$!
sleep 2
cleanup() { [ -n "${APP_PID:-}" ] && kill "$APP_PID" 2>/dev/null || true; kill "$XVFB_PID" 2>/dev/null || true; }
trap cleanup EXIT

dotnet "$DLL" >"$LOG_DIR/shot-app.log" 2>&1 &
APP_PID=$!
sleep "$WAIT"

if ! kill -0 "$APP_PID" 2>/dev/null; then
  echo "APP CRASHED"; tail -30 "$LOG_DIR/shot-app.log"; exit 1
fi

# Each remaining arg is a click at x:y, separated by 1.2s settles.
for c in "$@"; do
  x=${c%%:*}; y=${c##*:}
  xdotool mousemove "$x" "$y" click 1
  sleep 1.4
done
sleep 1
import -window root "$OUT"
echo "saved $OUT"
