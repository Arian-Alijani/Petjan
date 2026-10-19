#!/usr/bin/env bash
# live-correctness.sh — per-feature create/edit + two-client sync convergence suite against the LIVE server.
# Runs preflight first (read-only), then the correctness scenarios. Reports: artifacts/live-test/correctness-<run>.md|json
#
# Env (required): ANIMORA_LT_USER, ANIMORA_LT_PASSWORD
# Env (optional): ANIMORA_LT_SERVER_URL, ANIMORA_LT_TENANT, ANIMORA_LT_FILTER, ANIMORA_LT_KEEP_GOING=1, ANIMORA_LT_OUTPUT
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/_livetest-common.sh"
livetest_run preflight
livetest_run correctness
