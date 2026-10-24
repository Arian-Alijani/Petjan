#!/usr/bin/env bash
# live-capacity.sh — realistic concurrent load against the LIVE server with a read-only SSH hardware monitor.
# Reports: artifacts/live-test/capacity-<run>.md|json (planning reference only).
#
# Env (required): ANIMORA_LT_USER, ANIMORA_LT_PASSWORD
# Env (optional): ANIMORA_LT_SERVER_URL, ANIMORA_LT_TENANT (must contain TEST/LOAD/CAP)
#   Fixed levels      : ANIMORA_LT_LEVELS=1,2,4,8,16,32
#   Auto-ramp         : ANIMORA_LT_AUTO_RAMP=1 ANIMORA_LT_START_LEVEL=5 ANIMORA_LT_STEP=5
#                       ANIMORA_LT_MAX_LEVEL=200 ANIMORA_LT_BREAK_CONFIRM=2
#   Realism           : ANIMORA_LT_DURATION=20 (s per level), ANIMORA_LT_THINK_MIN_MS=2000,
#                       ANIMORA_LT_THINK_MAX_MS=9000, ANIMORA_LT_AMBIENT_POLL_SECONDS=5
#   Other             : ANIMORA_LT_REQUESTS=60 (only when DURATION=0), ANIMORA_LT_PROFILES=<filter>,
#                       ANIMORA_LT_MAX_DEVICES=64, ANIMORA_LT_P95_LIMIT_MS=2000,
#                       ANIMORA_LT_SSH=1 + ANIMORA_LT_SSH_USER + ANIMORA_LT_SSH_KEY [+ ANIMORA_LT_SSH_HOST/PORT]
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/_livetest-common.sh"
livetest_run preflight
livetest_run capacity
