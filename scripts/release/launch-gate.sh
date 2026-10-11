#!/usr/bin/env bash
# =============================================================================
# launch-gate.sh — launch-gate aggregator (G09, ARCH §19 "earned-quality bar").
#
# No "production-ready" label is declared in prose: it is EARNED when every ARCH §19 gate is green.
# This script aggregates every lane and EXITS NON-ZERO unless ALL of them are green:
#   * pg-gates.yml   jobs pg-correctness / pg-rls-hardening / restore-drill   (G07)
#   * perf-gates.yml jobs offline-budget / server-capacity / zero-internet /
#                         layering-audit / sync-behavioral                    (G08)
#   * desktop smoke  (scripts/sandbox/run.sh -> "SMOKE RUN OK")
#   * SBOM + native-binary inventory presence (scripts/release/make-release-manifest.sh output)
#   * zero-Internet BUILT-OUTPUT scan (G08 gate Z PROMOTED to a ship-blocker): no cdn / fonts.googleapis /
#     external endpoint in the packaged assets
#
# Gate table (the single source for LaunchGateManifestTests — keep the GATE| lines machine-parsable):
#   GATE|<letter>|<meaning>|<lane[,lane…]>
#
# Usage:
#   scripts/release/launch-gate.sh --dry-run                    list every §19 lane it would require (no execution)
#   scripts/release/launch-gate.sh --sha <commit> [--repo o/n]  query GitHub Actions for that commit (needs gh + token)
#   scripts/release/launch-gate.sh --results <file>             lane results from a file instead of GitHub
#                                                               (lines: "<workflow>:<job>=<conclusion>")
#   Local-lane inputs (all have defaults):
#     --smoke-log <file>        default artifacts/launch-smoke.log   (must contain "SMOKE RUN OK"; generated with --run-smoke)
#     --run-smoke               run scripts/sandbox/run.sh --no-build to produce the smoke log (needs a prebuilt Desktop + xvfb)
#     --manifest-dir <dir>      default artifacts/release-manifest   (sbom.cdx.json + native-inventory.json)
#     --output-dir <dir>        built/packaged output to scan for external endpoints (REQUIRED for a real run)
#   --report <file>             default artifacts/launch-gate-report.md
# Exit: 0 = every lane green (report written) · 1 = a lane red/missing · 2 = usage
# =============================================================================
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$REPO_ROOT"

# ── ARCH §19: the 18 gates, mapped to the lanes that prove them ──────────────
# Lane syntax: <workflow>:<job>  |  local:smoke | local:sbom | local:native-inventory | local:zero-internet-scan
GATES=(
  "GATE|A|Idempotency & financial-effect-once|pg-gates:pg-correctness"
  "GATE|D|Dependency & ordering|pg-gates:pg-correctness"
  "GATE|C|Conflict policy|pg-gates:pg-correctness"
  "GATE|N|Non-destructive sync|pg-gates:pg-correctness"
  "GATE|B|Immutable batch atomicity|pg-gates:pg-correctness"
  "GATE|L|Tenant isolation with hardening OFF (+ RLS ON proof)|pg-gates:pg-rls-hardening,pg-gates:pg-correctness"
  "GATE|P|Three planes (layering audit)|perf-gates:layering-audit"
  "GATE|O|Offline capability (offline budget + desktop smoke)|perf-gates:offline-budget,local:smoke"
  "GATE|K|Key-ring recovery|pg-gates:restore-drill,pg-gates:pg-correctness"
  "GATE|U|Licence authenticity & fail-closed|pg-gates:pg-correctness"
  "GATE|F|File lifecycle|pg-gates:pg-correctness"
  "GATE|S|Subscription single-engine|pg-gates:pg-correctness"
  "GATE|I|Reconnect storm|perf-gates:sync-behavioral"
  "GATE|M|Mixed fleet|perf-gates:sync-behavioral"
  "GATE|R|Rehearsed restore|pg-gates:restore-drill"
  "GATE|Z|Zero-Internet (built-output scan is a ship-blocker)|perf-gates:zero-internet,local:zero-internet-scan"
  "GATE|T|Measured capacity (planning reference only)|perf-gates:server-capacity"
  "GATE|PG|Journal/snapshot integrity (mandatory PostgreSQL)|pg-gates:pg-correctness"
)
# Supply-chain lanes required for every release in addition to the 18 gates (STACK §16).
EXTRA_LANES=("local:sbom" "local:native-inventory")

DRY=0; SHA=""; REPO=""; RESULTS=""; SMOKE_LOG="artifacts/launch-smoke.log"; RUN_SMOKE=0
MANIFEST_DIR="artifacts/release-manifest"; OUTPUT_DIR=""; REPORT="artifacts/launch-gate-report.md"
while [ $# -gt 0 ]; do
  case "$1" in
    --dry-run) DRY=1; shift ;;
    --sha) SHA="$2"; shift 2 ;;
    --repo) REPO="$2"; shift 2 ;;
    --results) RESULTS="$2"; shift 2 ;;
    --smoke-log) SMOKE_LOG="$2"; shift 2 ;;
    --run-smoke) RUN_SMOKE=1; shift ;;
    --manifest-dir) MANIFEST_DIR="$2"; shift 2 ;;
    --output-dir) OUTPUT_DIR="$2"; shift 2 ;;
    --report) REPORT="$2"; shift 2 ;;
    -h|--help) sed -n '2,33p' "$0"; exit 0 ;;
    *) echo "unknown arg: $1" >&2; exit 2 ;;
  esac
done

gate_field() { local IFS='|'; read -r -a f <<<"$1"; printf '%s' "${f[$2]}"; }

# ── --dry-run: list every lane, execute nothing ──────────────────────────────
if [ "$DRY" = "1" ]; then
  echo "ARCH §19 launch gate — ${#GATES[@]} gates, every lane REQUIRED (dry run, nothing executed):"
  for g in "${GATES[@]}"; do
    printf '  gate %-2s  %-58s <- %s\n' "$(gate_field "$g" 1)" "$(gate_field "$g" 2)" "$(gate_field "$g" 3)"
  done
  echo "supply-chain lanes (required for every release): ${EXTRA_LANES[*]}"
  uniq_lanes="$( { for g in "${GATES[@]}"; do gate_field "$g" 3; echo; done | tr ',' '\n'; printf '%s\n' "${EXTRA_LANES[@]}"; } | sed '/^$/d' | sort -u | tr '\n' ' ')"
  echo "distinct lanes: $uniq_lanes"
  echo "lanes: ${#GATES[@]} gates listed"
  exit 0
fi

# ── lane evaluation ──────────────────────────────────────────────────────────
declare -A CONCL=()      # "<workflow>:<job>" -> conclusion
declare -A EVID=()       # lane -> evidence pointer

load_results_file() {
  while IFS='=' read -r k v; do
    k="${k//[[:space:]]/}"; v="${v//[[:space:]]/}"
    [ -z "$k" ] || [[ "$k" == \#* ]] && continue
    CONCL["$k"]="$v"; EVID["$k"]="results file: $RESULTS"
  done <"$RESULTS"
}

load_from_github() {
  command -v gh >/dev/null 2>&1 || { echo "✗ gh CLI is required for --sha (or pass --results)" >&2; exit 2; }
  command -v python3 >/dev/null 2>&1 || { echo "✗ python3 is required" >&2; exit 2; }
  [ -n "$REPO" ] || REPO="$(git remote get-url origin | sed -E 's#^git@github\.com:#https://github.com/#; s#\.git$##; s#^https://github.com/##')"
  for wf in pg-gates perf-gates; do
    local run_json
    run_json="$(gh api "repos/$REPO/actions/workflows/$wf.yml/runs?head_sha=$SHA&per_page=20" 2>/dev/null || echo '{}')"
    # newest COMPLETED run for this exact commit
    local run_id
    run_id="$(printf '%s' "$run_json" | python3 -c '
import json,sys
d=json.load(sys.stdin); runs=[r for r in d.get("workflow_runs",[]) if r.get("status")=="completed"]
runs.sort(key=lambda r:r.get("run_number",0), reverse=True); print(runs[0]["id"] if runs else "")')"
    if [ -z "$run_id" ]; then echo "⚠ no completed $wf.yml run found for $SHA" >&2; continue; fi
    # Job display names start with the job title, not the id — resolve the id from the workflow file.
    local ids; ids="$(python3 - "$REPO_ROOT/$wf.yml" <<'PY'
import sys,re
txt=open(sys.argv[1],encoding="utf-8").read()
jobs=re.findall(r"^  ([a-z0-9-]+):\n    name: (.+)$", txt, re.M)
for jid,name in jobs: print(jid+"\t"+name.strip())
PY
)"
    local jobs_json; jobs_json="$(gh api "repos/$REPO/actions/runs/$run_id/jobs?per_page=100" 2>/dev/null || echo '{}')"
    while IFS=$'\t' read -r jid jname; do
      [ -n "$jid" ] || continue
      local c
      c="$(printf '%s' "$jobs_json" | python3 -c '
import json,sys
name=sys.argv[1]; d=json.load(sys.stdin)
m=[j for j in d.get("jobs",[]) if j.get("name","").strip()==name.strip()]
print(m[0].get("conclusion") or "" if m else "")' "$jname")"
      CONCL["$wf:$jid"]="${c:-missing}"; EVID["$wf:$jid"]="https://github.com/$REPO/actions/runs/$run_id"
    done <<<"$ids"
  done
}

if [ -n "$RESULTS" ]; then
  [ -f "$RESULTS" ] || { echo "✗ results file not found: $RESULTS" >&2; exit 2; }
  load_results_file
elif [ -n "$SHA" ]; then
  load_from_github
else
  echo "✗ need --sha <commit> (GitHub Actions) or --results <file>; use --dry-run to just list the lanes" >&2; exit 2
fi

# local lanes ---------------------------------------------------------------
if [ "$RUN_SMOKE" = "1" ]; then
  mkdir -p "$(dirname "$SMOKE_LOG")"
  chmod +x scripts/sandbox/run.sh 2>/dev/null || true
  scripts/sandbox/run.sh --no-build >"$SMOKE_LOG" 2>&1 || true
fi
local_lane() {
  case "$1" in
    smoke)
      if [ -f "$SMOKE_LOG" ] && grep -q "SMOKE RUN OK" "$SMOKE_LOG"; then echo "success|$SMOKE_LOG"; else echo "failure|$SMOKE_LOG (no 'SMOKE RUN OK')"; fi ;;
    sbom)
      if [ -s "$MANIFEST_DIR/sbom.cdx.json" ]; then echo "success|$MANIFEST_DIR/sbom.cdx.json"; else echo "failure|$MANIFEST_DIR/sbom.cdx.json missing"; fi ;;
    native-inventory)
      if [ -s "$MANIFEST_DIR/native-inventory.json" ]; then echo "success|$MANIFEST_DIR/native-inventory.json"; else echo "failure|$MANIFEST_DIR/native-inventory.json missing"; fi ;;
    zero-internet-scan)
      if [ -z "$OUTPUT_DIR" ] || [ ! -d "$OUTPUT_DIR" ]; then echo "failure|--output-dir missing/not found (built-output scan is a ship-blocker)"; return; fi
      if ZERO_INTERNET_SKIP_RUNTIME=1 scripts/perf/zero-internet.sh "$OUTPUT_DIR" >/dev/null 2>&1; then
        echo "success|artifacts/zero-internet.txt"; else echo "failure|artifacts/zero-internet.txt"; fi ;;
    *) echo "failure|unknown local lane '$1'" ;;
  esac
}

lane_state() { # lane -> "success|evidence" | "failure|evidence"
  local lane="$1"
  if [[ "$lane" == local:* ]]; then local_lane "${lane#local:}"; return; fi
  local c="${CONCL[$lane]:-missing}"
  if [ "$c" = "success" ]; then echo "success|${EVID[$lane]:-$lane}"; else echo "$c|${EVID[$lane]:-no result for $lane}"; fi
}

RED=0; ROWS=(); declare -A SEEN=()
for g in "${GATES[@]}"; do
  letter="$(gate_field "$g" 1)"; meaning="$(gate_field "$g" 2)"; lanes="$(gate_field "$g" 3)"
  ok=1; evid=()
  IFS=',' read -r -a arr <<<"$lanes"
  for lane in "${arr[@]}"; do
    if [ -z "${SEEN[$lane]:-}" ]; then SEEN["$lane"]="$(lane_state "$lane")"; fi
    st="${SEEN[$lane]%%|*}"; ev="${SEEN[$lane]#*|}"
    [ "$st" = "success" ] || ok=0
    evid+=("\`$lane\` → $st ($ev)")
  done
  if [ $ok -eq 1 ]; then ROWS+=("| $letter | $meaning | ✅ PASS | $(IFS='; '; echo "${evid[*]}") |")
  else ROWS+=("| $letter | $meaning | ❌ FAIL | $(IFS='; '; echo "${evid[*]}") |"); RED=1; fi
done
for lane in "${EXTRA_LANES[@]}"; do
  [ -n "${SEEN[$lane]:-}" ] || SEEN["$lane"]="$(lane_state "$lane")"
  st="${SEEN[$lane]%%|*}"; ev="${SEEN[$lane]#*|}"
  if [ "$st" = "success" ]; then ROWS+=("| supply-chain | ${lane#local:} | ✅ PASS | $ev |"); else ROWS+=("| supply-chain | ${lane#local:} | ❌ FAIL | $ev |"); RED=1; fi
done

if [ $RED -ne 0 ]; then
  echo "✗ LAUNCH GATE: RED — not every ARCH §19 lane is green. No report of record is written."
  printf '%s\n' "${ROWS[@]}" | grep '❌' || true
  exit 1
fi

mkdir -p "$(dirname "$REPORT")"
{
  echo "# Launch-gate report"
  echo
  echo "- Date (UTC): $(date -u +%Y-%m-%dT%H:%M:%SZ)"
  echo "- Commit: ${SHA:-$(git rev-parse HEAD 2>/dev/null || echo unknown)}"
  echo "- Result: **ALL ${#GATES[@]} ARCH §19 gates PASS** + SBOM + native-binary inventory present"
  echo
  echo "> Earned, not declared (ARCH §19): this report exists only because every lane below was green."
  echo "> Capacity (gate T) is a *planning reference only* (A-RULE 035), not a guaranteed user count."
  echo
  echo "| Gate | Proves | Result | Evidence |"
  echo "|---|---|---|---|"
  printf '%s\n' "${ROWS[@]}"
} >"$REPORT"
echo "✓ LAUNCH GATE: GREEN — ${#GATES[@]}/${#GATES[@]} gates PASS → $REPORT"
