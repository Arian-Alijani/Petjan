#!/usr/bin/env bash
# =============================================================================
# install-prereqs.sh  —  install everything needed to build/run Animora here
# =============================================================================
# Idempotent. Safe to re-run. Installs:
#   1. .NET 10 SDK  -> $HOME/.dotnet   (via official dotnet-install.sh, no root)
#   2. xvfb          -> for headless GUI smoke-runs (apt, needs sudo; optional)
#
# Fast by design:
#   * SDK-only download, extracted straight onto the root disk (TMPDIR is moved
#     off the 493 MiB /tmp tmpfs by _env.sh, so the 240 MB tarball never fills
#     RAM/tmpfs — the exact failure mode that breaks a naive install here).
#   * Skips work already done (checks `dotnet --list-sdks` and `which Xvfb`).
#
# Usage:
#   scripts/sandbox/install-prereqs.sh            # SDK (+ xvfb if sudo works)
#   scripts/sandbox/install-prereqs.sh --no-xvfb  # SDK only
# =============================================================================

source "$(dirname "${BASH_SOURCE[0]}")/_env.sh"

WANT_XVFB=1
[ "${1:-}" = "--no-xvfb" ] && WANT_XVFB=0

LOG="$LOG_DIR/install-prereqs.log"
: >"$LOG"
# Mirror everything to the log; keep exit code visible even on crash.
exec > >(tee -a "$LOG") 2>&1

log "Install log: $LOG"

# -----------------------------------------------------------------------------
# 1. .NET SDK
# -----------------------------------------------------------------------------
if has_dotnet; then
  ok ".NET SDK already present: $(dotnet --version)  (skipping)"
else
  log "Installing .NET SDK channel $DOTNET_CHANNEL into $DOTNET_ROOT ..."
  INSTALLER="$TMPDIR/dotnet-install.sh"

  if ! curl -fsSL https://dot.net/v1/dotnet-install.sh -o "$INSTALLER"; then
    err "Could not download dotnet-install.sh (network?)."
    exit 4
  fi
  chmod +x "$INSTALLER"

  # TMPDIR already points at the root disk (see _env.sh) so extraction of the
  # ~240 MB tarball cannot exhaust the tmpfs. --install-dir keeps it per-user.
  if ! "$INSTALLER" --channel "$DOTNET_CHANNEL" --install-dir "$DOTNET_ROOT"; then
    err "dotnet-install.sh failed — see log above."
    exit 4
  fi

  if ! has_dotnet; then
    err "SDK installed but 'dotnet --list-sdks' still fails."
    exit 4
  fi
  ok ".NET SDK installed: $(dotnet --version)"
fi

# -----------------------------------------------------------------------------
# 2. xvfb (virtual framebuffer) — only needed to actually *run* the GUI.
#    Build and test do NOT need it, so failure here is non-fatal.
# -----------------------------------------------------------------------------
if [ "$WANT_XVFB" = "1" ]; then
  if command -v Xvfb >/dev/null 2>&1; then
    ok "xvfb already installed (skipping)"
  else
    log "Installing xvfb (needs sudo; non-fatal if it fails) ..."
    if sudo -n true 2>/dev/null || sudo true 2>/dev/null; then
      if sudo apt-get update -qq && sudo apt-get install -y -qq xvfb; then
        ok "xvfb installed."
      else
        warn "xvfb install failed — headless RUN unavailable, build/test still work."
      fi
    else
      warn "No sudo — skipping xvfb. build/test still work; run needs a display."
    fi
  fi
else
  log "--no-xvfb: skipping xvfb."
fi

# -----------------------------------------------------------------------------
# 3. Warm the NuGet cache with a restore (makes the first build fast).
# -----------------------------------------------------------------------------
log "Restoring NuGet packages (into $NUGET_PACKAGES) ..."
if dotnet restore "$SOLUTION" -v minimal; then
  ok "Restore complete."
else
  err "Restore failed — see log above."
  exit 5
fi

ok "Prerequisites ready. Next: scripts/sandbox/build.sh"
