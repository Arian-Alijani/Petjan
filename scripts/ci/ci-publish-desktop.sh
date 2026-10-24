#!/usr/bin/env bash
# =============================================================================
# ci-publish-desktop.sh  —  produce a self-contained Windows build of the app
# =============================================================================
# CI equivalent of scripts/windows/publish-portable.ps1: publishes
# Animora.Desktop as a Release, self-contained, single-file Windows app so the
# workflow can upload it as a downloadable artifact.
#
# Runs on the GitHub *windows* runner (self-contained win-* publish + the native
# SQLCipher/e_sqlite3 binaries require building on Windows). Invoked via Git Bash
# that ships on windows-latest.
#
# Usage:
#   scripts/ci/ci-publish-desktop.sh <runtime> <output-dir>
#     runtime    = win-x64 | win-arm64   (default: win-x64)
#     output-dir = where the published app folder is written (default: ./publish/app)
#
# Notes:
#   * IncludeNativeLibrariesForSelfExtract is intentionally OFF so the native
#     SQLite binaries sit next to the exe and load reliably (same as the PS1).
#   * EnableAvaloniaXamlCompilation=true avoids the "No precompiled XAML"
#     startup crash.
# =============================================================================

set -uo pipefail

SELF_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=_projects.sh
source "$SELF_DIR/_projects.sh"

RUNTIME="${1:-win-x64}"
OUT_DIR="${2:-$REPO_ROOT/publish/app}"

case "$RUNTIME" in win-x64|win-arm64) ;; *) echo "[ci-publish] Unknown runtime '$RUNTIME' (expected win-x64|win-arm64)" >&2; exit 2 ;; esac

PROJ="$(abspath src/Animora.Desktop/Animora.Desktop.csproj)"
echo "[ci-publish] runtime=$RUNTIME  project=$(basename "$PROJ")  out=$OUT_DIR"

summary() { [ -n "${GITHUB_STEP_SUMMARY:-}" ] && printf '%s\n' "$*" >>"$GITHUB_STEP_SUMMARY" || true; }

# Clean THIS project's intermediate output first so a stale obj/ can never make
# the build skip CoreCompile and ship an assembly missing the compiled Avalonia
# XAML resource (the "No precompiled XAML found for Animora.Desktop.App" crash).
# Deleting bin/ and obj/ also GUARANTEES a full (non-incremental) compile, so we
# do NOT pass the `dotnet build` switch `--no-incremental` to `dotnet publish`:
# it is not accepted on every SDK (SDK 10.0.4xx leaks it straight to MSBuild,
# which aborts with "MSB1001: Unknown switch"). The clean below already provides
# the identical "never reuse a stale compile" guarantee.
PROJ_DIR="$(dirname "$PROJ")"
echo "[ci-publish] cleaning stale bin/ and obj/ under $PROJ_DIR"
rm -rf "$PROJ_DIR/bin" "$PROJ_DIR/obj"

START=$(date +%s)
dotnet publish "$PROJ" \
  -c Release \
  -r "$RUNTIME" \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:EnableAvaloniaXamlCompilation=true \
  -p:DebugType=none \
  -p:DebugSymbols=false \
  -o "$OUT_DIR"
RC=$?
ELAPSED=$(( $(date +%s) - START ))

summary "### 🪟 Desktop publish ($RUNTIME, Release self-contained)"
summary ""
if [ "$RC" -ne 0 ]; then
  echo "[ci-publish] FAILED (exit $RC) after ${ELAPSED}s" >&2
  summary "**Result:** ❌ Publish failed (exit $RC) after ${ELAPSED}s."
  exit "$RC"
fi

# The exe name is stable; prove it exists.
EXE="$OUT_DIR/Animora.Desktop.exe"
if [ ! -f "$EXE" ]; then
  echo "[ci-publish] Publish succeeded but Animora.Desktop.exe not found in $OUT_DIR" >&2
  summary "**Result:** ❌ Publish finished but \`Animora.Desktop.exe\` is missing."
  exit 1
fi

SIZE_MB=$(( $(wc -c <"$EXE") / 1048576 ))
echo "[ci-publish] OK in ${ELAPSED}s  exe=${EXE} (~${SIZE_MB} MB)"
summary "**Result:** ✅ Published in ${ELAPSED}s."
summary ""
summary "- Runtime: \`$RUNTIME\`"
summary "- Output: \`Animora.Desktop.exe\` (~${SIZE_MB} MB, self-contained single-file)"
summary "- Download it from this run's **Artifacts** section."
exit 0
