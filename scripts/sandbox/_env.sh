#!/usr/bin/env bash
# =============================================================================
# _env.sh  —  shared environment for all Animora sandbox scripts
# =============================================================================
# Sourced by every other script in this directory. Encodes EVERY constraint of
# this specific sandbox (Debian 13, ~985 MiB RAM, /tmp = 493 MiB tmpfs, no .NET
# SDK preinstalled, no display). Do not run directly.
#
# Design rules honoured here:
#   * RAM is tiny (~985 MiB) -> workstation GC, single-process MSBuild, no
#     Roslyn build server. Server GC would try to reserve a heap per core and
#     get OOM-killed.
#   * /tmp is a 493 MiB tmpfs shared with RAM -> redirect ALL temp/NuGet/dotnet
#     scratch onto the 18 GB root disk under the repo, never /tmp.
#   * The SDK is installed per-user under the repo (no root, no apt for .NET),
#     so builds are reproducible and the ~240 MB download is cached on disk.
# =============================================================================

set -euo pipefail

# --- repo layout -------------------------------------------------------------
# scripts/sandbox/_env.sh -> repo root is two levels up.
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
export REPO_ROOT

SOLUTION="$REPO_ROOT/Animora.sln"
DESKTOP_PROJ="$REPO_ROOT/src/Animora.Desktop/Animora.Desktop.csproj"
DESKTOP_DLL_REL="src/Animora.Desktop/bin"   # + /<config>/net10.0/Animora.Desktop.dll
export SOLUTION DESKTOP_PROJ DESKTOP_DLL_REL

# --- toolchain location (per-user, on disk) ---------------------------------
DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
export DOTNET_ROOT
export PATH="$DOTNET_ROOT:$PATH"

# --- disk-backed scratch (NEVER the /tmp tmpfs) ------------------------------
# All of these live on the 18 GB root fs so a big NuGet restore / SDK unpack
# cannot exhaust the 493 MiB tmpfs (which also eats into RAM).
SCRATCH="$REPO_ROOT/.sandbox"
export TMPDIR="$SCRATCH/tmp"
export NUGET_PACKAGES="$SCRATCH/nuget"
export DOTNET_CLI_HOME="$SCRATCH/dotnet-home"
LOG_DIR="$SCRATCH/logs"
mkdir -p "$TMPDIR" "$NUGET_PACKAGES" "$DOTNET_CLI_HOME" "$LOG_DIR"
export LOG_DIR

# --- .NET runtime / build behaviour ------------------------------------------
export DOTNET_CLI_TELEMETRY_OPTOUT=1        # no network telemetry
export DOTNET_NOLOGO=1                       # quieter, faster startup
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export DOTNET_gcServer=0                      # workstation GC (low RAM)
export DOTNET_GCHeapHardLimit=0x24000000      # ~576 MiB hard cap on GC heap (B03c: Desktop now compiles XAML against the full backend graph)

# MSBuild flags reused by build/test/run. Single process + no shared compiler
# server keeps peak memory well under the 985 MiB ceiling.
MSBUILD_LOWMEM=(
  -m:1
  -p:UseSharedCompilation=false
  -p:BuildInParallel=false
  -p:ServerGarbageCollection=false
  -nodeReuse:false
)
export MSBUILD_LOWMEM

# Pinned SDK channel (project targets net10.0; Directory.Build.props LangVersion 14).
DOTNET_CHANNEL="10.0"
export DOTNET_CHANNEL

# --- pretty logging ----------------------------------------------------------
# Colours only when attached to a TTY (kept out of log files).
if [ -t 1 ]; then
  C_RED=$'\033[31m'; C_GRN=$'\033[32m'; C_YLW=$'\033[33m'; C_BLU=$'\033[34m'; C_RST=$'\033[0m'
else
  C_RED=''; C_GRN=''; C_YLW=''; C_BLU=''; C_RST=''
fi
log()  { printf '%s[env]%s %s\n' "$C_BLU" "$C_RST" "$*"; }
ok()   { printf '%s[ ok ]%s %s\n' "$C_GRN" "$C_RST" "$*"; }
warn() { printf '%s[warn]%s %s\n' "$C_YLW" "$C_RST" "$*" >&2; }
err()  { printf '%s[FAIL]%s %s\n' "$C_RED" "$C_RST" "$*" >&2; }

# has_dotnet: true only if a real dotnet with an SDK is on PATH.
has_dotnet() { command -v dotnet >/dev/null 2>&1 && dotnet --list-sdks >/dev/null 2>&1; }

# require_dotnet: abort with a clear hint if the SDK is missing.
require_dotnet() {
  if ! has_dotnet; then
    err "No .NET SDK found. Run: scripts/sandbox/install-prereqs.sh"
    exit 3
  fi
}
