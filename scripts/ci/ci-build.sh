#!/usr/bin/env bash
# =============================================================================
# ci-build.sh  —  scoped build for Animora on a GitHub Actions runner
# =============================================================================
# Builds only the projects for the requested SCOPE, in the requested
# CONFIGURATION. Designed for GitHub-hosted runners (plenty of RAM, so it does
# NOT apply the sandbox's low-memory clamps) while keeping the SAME project
# groupings the local sandbox scripts use (see scripts/ci/_projects.sh).
#
# Usage:
#   scripts/ci/ci-build.sh <scope> <configuration>
#     scope         = all | server | desktop | core
#     configuration = Debug | Release   (default: Debug)
#
# Results:
#   * Human-readable status printed to stdout (captured in the job log).
#   * When $GITHUB_STEP_SUMMARY is set, a Markdown summary row is appended so
#     the model can read the outcome straight from the run's Summary page.
#   * Nonzero exit on the first failing project (real dotnet exit code).
# =============================================================================

set -uo pipefail

SELF_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=_projects.sh
source "$SELF_DIR/_projects.sh"

SCOPE="${1:-all}"
CONFIG="${2:-Debug}"

# --- logging helpers (color only on TTY) -------------------------------------
if [ -t 1 ]; then C_RED=$'\033[31m'; C_GRN=$'\033[32m'; C_BLU=$'\033[34m'; C_RST=$'\033[0m'
else C_RED=''; C_GRN=''; C_BLU=''; C_RST=''; fi
log()  { printf '%s[ci-build]%s %s\n' "$C_BLU" "$C_RST" "$*"; }
ok()   { printf '%s[  ok   ]%s %s\n' "$C_GRN" "$C_RST" "$*"; }
err()  { printf '%s[ FAIL  ]%s %s\n' "$C_RED" "$C_RST" "$*" >&2; }

summary() { [ -n "${GITHUB_STEP_SUMMARY:-}" ] && printf '%s\n' "$*" >>"$GITHUB_STEP_SUMMARY" || true; }

# --- resolve which projects this scope builds --------------------------------
PROJECTS=()
case "$SCOPE" in
  all)
    # Whole solution in one shot — simplest and lets MSBuild parallelise.
    PROJECTS=("$SOLUTION") ;;
  server)
    PROJECTS=("${SERVER_PROJECTS[@]}") ;;
  desktop)
    PROJECTS=("${DESKTOP_PROJECTS[@]}") ;;
  core)
    PROJECTS=("${CORE_PROJECTS[@]}") ;;
  *)
    err "Unknown scope: '$SCOPE' (expected all|server|desktop|core)"; exit 2 ;;
esac

case "$CONFIG" in
  Debug|Release) ;;
  *) err "Unknown configuration: '$CONFIG' (expected Debug|Release)"; exit 2 ;;
esac

log "scope=$SCOPE  configuration=$CONFIG  projects=${#PROJECTS[@]}"
summary "### 🔨 Build — scope \`$SCOPE\` ($CONFIG)"
summary ""
summary "| Project | Result |"
summary "|---|---|"

RC=0
START=$(date +%s)
for p in "${PROJECTS[@]}"; do
  # $SOLUTION is already absolute; csproj paths are repo-relative.
  if [ "$p" = "$SOLUTION" ]; then TARGET="$p"; else TARGET="$(abspath "$p")"; fi
  name="$(basename "$TARGET")"
  log "building $name ..."

  dotnet build "$TARGET" -c "$CONFIG" --nologo -v minimal
  rc=$?
  if [ "$rc" -eq 0 ]; then
    ok "$name"
    summary "| \`$name\` | ✅ OK |"
  else
    err "$name (exit $rc)"
    summary "| \`$name\` | ❌ FAILED (exit $rc) |"
    RC=$rc
  fi
done
ELAPSED=$(( $(date +%s) - START ))

summary ""
if [ "$RC" -eq 0 ]; then
  ok "BUILD OK ($SCOPE/$CONFIG) in ${ELAPSED}s"
  summary "**Result:** ✅ Build succeeded in ${ELAPSED}s."
else
  err "BUILD FAILED ($SCOPE/$CONFIG, exit $RC) after ${ELAPSED}s"
  summary "**Result:** ❌ Build failed (exit $RC) after ${ELAPSED}s."
fi
exit "$RC"
