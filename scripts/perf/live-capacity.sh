#!/usr/bin/env bash
# live-capacity.sh — realistic concurrent load against the LIVE server with a read-only SSH hardware monitor.
# Reports: artifacts/live-test/capacity-<run>.md|json (planning reference only).
#
# Env (required): ANIMORA_LT_USER, ANIMORA_LT_PASSWORD
# Env (optional): ANIMORA_LT_SERVER_URL, ANIMORA_LT_TENANT (must contain TEST/LOAD/CAP), ANIMORA_LT_LEVELS=1,2,4,8,16,32,
#                 ANIMORA_LT_REQUESTS=60, ANIMORA_LT_PROFILES=<name filter>, ANIMORA_LT_MAX_DEVICES=8, ANIMORA_LT_P95_LIMIT_MS=2000,
#                 ANIMORA_LT_SSH=1 + ANIMORA_LT_SSH_USER + ANIMORA_LT_SSH_KEY [+ ANIMORA_LT_SSH_HOST/PORT] for server hardware metrics
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/_livetest-common.sh"
livetest_run preflight
livetest_run capacity
