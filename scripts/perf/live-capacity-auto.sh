#!/usr/bin/env bash
# live-capacity-auto.sh — the "how many users can this server actually hold?" run.
#
# Instead of a hand-picked level list, this ramps concurrency automatically (default 5 → 200 in steps of 5) and stops
# when the server has been unhealthy for N consecutive levels, so the ceiling is measured rather than guessed.
# Every virtual user pauses 2–9 s between actions and keeps the production 5 s sync poll, so the result is a real
# clinic-desktop count, not a synthetic request rate.
#
# Env (required): ANIMORA_LT_USER, ANIMORA_LT_PASSWORD
# Env (optional): ANIMORA_LT_SERVER_URL, ANIMORA_LT_TENANT,
#                 ANIMORA_LT_START_LEVEL (5), ANIMORA_LT_STEP (5), ANIMORA_LT_MAX_LEVEL (200),
#                 ANIMORA_LT_BREAK_CONFIRM (2), ANIMORA_LT_DURATION (20),
#                 ANIMORA_LT_THINK_MIN_MS (2000), ANIMORA_LT_THINK_MAX_MS (9000),
#                 ANIMORA_LT_AMBIENT_POLL_SECONDS (5), ANIMORA_LT_MAX_DEVICES (64),
#                 ANIMORA_LT_P95_LIMIT_MS (2000), ANIMORA_LT_PROFILES,
#                 ANIMORA_LT_SSH=1 + ANIMORA_LT_SSH_USER + ANIMORA_LT_SSH_KEY [+ ANIMORA_LT_SSH_HOST/PORT]
set -euo pipefail

export ANIMORA_LT_AUTO_RAMP="${ANIMORA_LT_AUTO_RAMP:-1}"
export ANIMORA_LT_START_LEVEL="${ANIMORA_LT_START_LEVEL:-5}"
export ANIMORA_LT_STEP="${ANIMORA_LT_STEP:-5}"
export ANIMORA_LT_MAX_LEVEL="${ANIMORA_LT_MAX_LEVEL:-200}"
export ANIMORA_LT_BREAK_CONFIRM="${ANIMORA_LT_BREAK_CONFIRM:-2}"
export ANIMORA_LT_DURATION="${ANIMORA_LT_DURATION:-20}"
export ANIMORA_LT_THINK_MIN_MS="${ANIMORA_LT_THINK_MIN_MS:-2000}"
export ANIMORA_LT_THINK_MAX_MS="${ANIMORA_LT_THINK_MAX_MS:-9000}"
export ANIMORA_LT_AMBIENT_POLL_SECONDS="${ANIMORA_LT_AMBIENT_POLL_SECONDS:-5}"

source "$(dirname "${BASH_SOURCE[0]}")/_livetest-common.sh"
livetest_run preflight
livetest_run capacity
