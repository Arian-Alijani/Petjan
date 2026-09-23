#!/usr/bin/env bash
# shot-seq.sh OUT.png "click:1420:317" "sleep:2" "click:900:500" ...
set -uo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/_env.sh"
OUT="${1:-$REPO_ROOT/.sandbox/seq.png}"; shift || true
DLL="$REPO_ROOT/$DESKTOP_DLL_REL/Debug/net10.0/Animora.Desktop.dll"
DISPLAY_NUM="${DISPLAY_NUM:-99}"
export DISPLAY=":$DISPLAY_NUM"
Xvfb "$DISPLAY" -screen 0 "1600x1000x24" >/dev/null 2>&1 &
XVFB_PID=$!
sleep 2
cleanup() { [ -n "${APP_PID:-}" ] && kill "$APP_PID" 2>/dev/null || true; kill "$XVFB_PID" 2>/dev/null || true; }
trap cleanup EXIT
dotnet "$DLL" >"$LOG_DIR/shot-app-$DISPLAY_NUM.log" 2>&1 &
APP_PID=$!
sleep 9
if ! kill -0 "$APP_PID" 2>/dev/null; then echo "APP CRASHED"; tail -30 "$LOG_DIR/shot-app-$DISPLAY_NUM.log"; exit 1; fi
for step in "$@"; do
  kind="${step%%:*}"; rest="${step#*:}"
  case "$kind" in
    click) x="${rest%%:*}"; y="${rest##*:}"; xdotool mousemove "$x" "$y" click 1 ;;
    move)  x="${rest%%:*}"; y="${rest##*:}"; xdotool mousemove "$x" "$y" ;;
    key)   xdotool key "$rest" ;;
    sleep) sleep "$rest" ;;
    shot)  import -window root "$rest" ;;
  esac
  sleep 0.8
done
import -window root "$OUT"
echo "saved $OUT"
