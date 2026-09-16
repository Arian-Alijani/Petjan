#!/usr/bin/env bash
# =============================================================================
# clean.sh  —  remove build output (and optionally caches)
# =============================================================================
#   scripts/sandbox/clean.sh          # bin/ + obj/ only (fast)
#   scripts/sandbox/clean.sh --all    # also NuGet cache + logs + scratch tmp
#
# Never deletes the installed SDK ($HOME/.dotnet). --all only clears the
# repo-local .sandbox scratch, so a rebuild re-restores from the network.
# =============================================================================

source "$(dirname "${BASH_SOURCE[0]}")/_env.sh"

log "Removing bin/ and obj/ ..."
find "$REPO_ROOT/src" -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} + 2>/dev/null || true
ok "bin/ and obj/ removed."

if [ "${1:-}" = "--all" ]; then
  log "Clearing NuGet cache, logs and scratch tmp ..."
  rm -rf "$NUGET_PACKAGES" "$LOG_DIR" "$TMPDIR"
  mkdir -p "$NUGET_PACKAGES" "$LOG_DIR" "$TMPDIR"
  ok "Caches cleared (SDK kept). Next build will re-restore."
fi
