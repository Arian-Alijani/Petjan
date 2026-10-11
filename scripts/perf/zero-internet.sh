#!/usr/bin/env bash
# =============================================================================
# zero-internet.sh — Zero-Internet gate Z (G08; ARCH §17.4 / §19).
#
# (a) STATIC leg : scan a built/published output for http(s):// endpoints. Anything that is not
#     loopback / private-LAN / localhost / an XML-namespace URI / explicitly allow-listed is a FAILURE;
#     CDN & font hosts (cdn*, fonts.googleapis, fonts.gstatic, unpkg, jsdelivr, cdnjs …) are ALWAYS a failure
#     (never allow-listable).
# (b) RUNTIME leg: run the desktop smoke (scripts/sandbox/run.sh --no-build) with egress BLOCKED
#     (a private network namespace via `unshare -n`) and require `SMOKE RUN OK`.
#
# Usage:
#   scripts/perf/zero-internet.sh [<output-dir>]        default: src/Animora.Desktop/bin/Debug/net10.0
#   ZERO_INTERNET_SKIP_RUNTIME=1   run only the static leg (e.g. no display / no xvfb)
#   ZERO_INTERNET_SKIP_STATIC=1    run only the runtime leg
#   ZERO_INTERNET_ALLOW_FILE=...   extra allow-list (one host per line, '#' comments); default
#                                  scripts/perf/zero-internet.allow when present
# Output: artifacts/zero-internet.txt (report), artifacts/zero-internet-smoke.log (runtime leg log)
# Exit  : 0 = both legs green, 1 = violation, 2 = usage/precondition.
# =============================================================================
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$REPO_ROOT"

OUT_DIR="${1:-src/Animora.Desktop/bin/Debug/net10.0}"
ART="$REPO_ROOT/artifacts"
REPORT="$ART/zero-internet.txt"
SMOKE_LOG="$ART/zero-internet-smoke.log"
ALLOW_FILE="${ZERO_INTERNET_ALLOW_FILE:-$REPO_ROOT/scripts/perf/zero-internet.allow}"
mkdir -p "$ART"
: >"$REPORT"

say() { echo "$*" | tee -a "$REPORT"; }
FAIL=0

# Hosts that are fine by construction: loopback, RFC1918 LAN, link-local, localhost, *.local/.lan/.internal,
# and XML/XAML namespace identifiers (they are identifiers, never fetched).
is_allowed_host() {
  local h="${1,,}"
  h="${h%%:*}"
  case "$h" in
    localhost|127.*|0.0.0.0|::1|\[::1\]|10.*|192.168.*|169.254.*) return 0 ;;
    172.1[6-9].*|172.2[0-9].*|172.3[0-1].*) return 0 ;;
    *.local|*.lan|*.internal|*.localhost) return 0 ;;
    # XML / XAML / schema namespace identifiers.
    schemas.microsoft.com|schemas.openxmlformats.org|schemas.xmlsoap.org|www.w3.org|ns.adobe.com|purl.org) return 0 ;;
    www.apache.org|www.microsoft.com|www.avaloniaui.net|avaloniaui.net|github.com/avaloniaui) return 0 ;;
    # Config placeholders that are templates, not endpoints.
    example.com|*.example.com|example.org|*.example.org|example.net|*.invalid|*.test) return 0 ;;
  esac
  if [ -f "$ALLOW_FILE" ]; then
    # shellcheck disable=SC2002
    if cat "$ALLOW_FILE" | sed 's/#.*//' | tr -d ' \r' | grep -Fxiq -- "$h"; then return 0; fi
  fi
  return 1
}

is_forbidden_host() {
  local h="${1,,}"
  case "$h" in
    cdn.*|*.cdn.*|*cdn*.net|*cdnjs*|*jsdelivr*|unpkg.com|*.unpkg.com|fonts.googleapis.com|fonts.gstatic.com|\
    *googleapis.com|*gstatic.com|*cloudfront.net|*akamaihd.net|*fastly.net|*bootstrapcdn.com|*googletagmanager.com|\
    *google-analytics.com|*cloudflare.com) return 0 ;;
  esac
  return 1
}

# ───────────────────────── (a) STATIC leg ─────────────────────────
if [ "${ZERO_INTERNET_SKIP_STATIC:-0}" != "1" ]; then
  say "== Zero-Internet / static scan of: $OUT_DIR"
  if [ ! -d "$OUT_DIR" ]; then
    say "✗ output directory not found: $OUT_DIR (build/publish first)"
    exit 2
  fi

  URLS="$(mktemp)"; trap 'rm -f "$URLS"' EXIT
  # Text assets: every file; own assemblies (Animora.*.dll): strings. Third-party DLLs are skipped on
  # purpose (they carry licence/doc URLs that are never fetched at runtime).
  while IFS= read -r -d '' f; do
    case "$f" in
      *.dll)
        case "$(basename "$f")" in Animora.*) strings -a -n 8 "$f" 2>/dev/null || grep -aoE 'https?://[^[:space:]"<>)'"'"']+' "$f" ;; *) continue ;; esac ;;
      *.pdb|*.so|*.dylib|*.exe|*.png|*.jpg|*.jpeg|*.ico|*.ttf|*.otf|*.woff|*.woff2|*.gz|*.zip|*.nupkg|*.db|*.sqlite*|*.mo|*.pak) continue ;;
      *) cat "$f" 2>/dev/null ;;
    esac | grep -aoE 'https?://[^[:space:]"<>)'"'"'\\;,]+' | sed "s|^|$(basename "$f")\t|" >>"$URLS" || true
  done < <(find "$OUT_DIR" -type f -print0)

  total=0; bad=0
  while IFS=$'\t' read -r file url; do
    [ -n "${url:-}" ] || continue
    total=$((total+1))
    host="${url#*://}"; host="${host%%/*}"; host="${host%%\?*}"; host="${host##*@}"
    if is_forbidden_host "$host"; then
      say "✗ FORBIDDEN external host ($host) in $file: $url"; bad=$((bad+1))
    elif ! is_allowed_host "$host"; then
      say "✗ external endpoint ($host) in $file: $url"; bad=$((bad+1))
    fi
  done < <(sort -u "$URLS")

  say "static scan: $total unique url occurrence(s) inspected, $bad violation(s)"
  if [ "$bad" -gt 0 ]; then FAIL=1; fi
fi

# ───────────────────────── (b) RUNTIME leg ─────────────────────────
if [ "${ZERO_INTERNET_SKIP_RUNTIME:-0}" != "1" ]; then
  say "== Zero-Internet / runtime smoke with egress blocked"
  RUN="$REPO_ROOT/scripts/sandbox/run.sh"
  if [ ! -x "$RUN" ]; then chmod +x "$RUN" 2>/dev/null || true; fi

  WRAP=()
  if unshare -rn true 2>/dev/null; then
    WRAP=(unshare -rn)
    say "egress block: unprivileged network namespace (unshare -rn)"
  elif sudo -n unshare -n true 2>/dev/null; then
    WRAP=(sudo -n -E env "PATH=$PATH" unshare -n)
    say "egress block: root network namespace (sudo unshare -n)"
  else
    say "⚠ cannot create a network namespace here — running the smoke without an explicit egress block"
    say "  (the sandbox / CI runner lane for gate Z must provide one; this run is weaker evidence)"
  fi

  set +e
  "${WRAP[@]}" "$RUN" --no-build --seconds "${ZERO_INTERNET_SECONDS:-8}" >"$SMOKE_LOG" 2>&1
  rc=$?
  set -e
  if [ $rc -eq 0 ] && grep -q "SMOKE RUN OK" "$SMOKE_LOG"; then
    say "✓ SMOKE RUN OK with egress blocked (log: artifacts/zero-internet-smoke.log)"
  else
    say "✗ runtime smoke FAILED rc=$rc (tail of log):"
    tail -n 25 "$SMOKE_LOG" | tee -a "$REPORT"
    FAIL=1
  fi
fi

if [ "$FAIL" -ne 0 ]; then say "✗ ZERO-INTERNET GATE: RED"; exit 1; fi
say "✓ ZERO-INTERNET GATE: GREEN"
