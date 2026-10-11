#!/usr/bin/env bash
# =============================================================================
# gh-ci.sh — drive Animora's GitHub Actions CI from inside this sandbox
# =============================================================================
# WHY: compiling .NET 10 on this ~985 MiB box is slow and memory-fragile. This
# wrapper lets the agent push the heavy build/test work onto GitHub's runners
# and read the logs + results back, all with the `gh` CLI that is already
# authenticated in this sandbox (gh auth status -> Arian-Alijani).
#
# It is a thin, SAFE, discoverable layer over `gh workflow run` / `gh run ...`
# so the agent never has to remember flag combinations.
#
# ---------------------------------------------------------------------------
# COMMANDS
# ---------------------------------------------------------------------------
#   build [OPTS]        Trigger CI and (by default) wait + stream the result.
#   watch [RUN_ID]      Watch a run live (latest CI run if RUN_ID omitted).
#   logs  [RUN_ID]      Print the full logs of a run (latest if omitted).
#   failed [RUN_ID]     Print ONLY the failed-step logs (latest if omitted).
#   results [RUN_ID]    Download + summarise the .trx test results of a run.
#   list                List recent CI runs (id, status, scope, branch).
#   status [RUN_ID]     One-line status of a run (latest if omitted).
#   rerun [RUN_ID]      Re-run the failed jobs of a run (latest if omitted).
#   cancel [RUN_ID]     Cancel a run (latest if omitted).
#   open [RUN_ID]       Print the web URL of a run (latest if omitted).
#   help                This help.
#
# ---------------------------------------------------------------------------
# `build` OPTIONS  (map 1:1 to the ci.yml workflow_dispatch inputs)
# ---------------------------------------------------------------------------
#   --scope   all|app|server|build-only|restore   (default: all)
#   --config  Debug|Release                       (default: Debug)
#   --os      linux|windows|both                  (default: linux)
#   --filter  <xUnit FullyQualifiedName substring> (default: none)
#   --no-postgres     run the server track WITHOUT a real PostgreSQL service
#   --ref     <branch>   branch to run the workflow from (default: current)
#   --no-wait         fire-and-forget: trigger and return immediately
#
# ---------------------------------------------------------------------------
# TYPICAL AGENT FLOWS
# ---------------------------------------------------------------------------
#   # Full build + all tests on GitHub, wait for the verdict:
#   scripts/sandbox/gh-ci.sh build
#
#   # Just the desktop app + pure tests, Release, on Windows + Linux:
#   scripts/sandbox/gh-ci.sh build --scope app --config Release --os both
#
#   # Only the server track against a real PostgreSQL:
#   scripts/sandbox/gh-ci.sh build --scope server
#
#   # Run a single test class quickly:
#   scripts/sandbox/gh-ci.sh build --scope app --filter Layering
#
#   # Trigger without waiting, then inspect later:
#   scripts/sandbox/gh-ci.sh build --no-wait
#   scripts/sandbox/gh-ci.sh watch
#   scripts/sandbox/gh-ci.sh failed          # only the failing step logs
#   scripts/sandbox/gh-ci.sh results         # download + summarise .trx
# =============================================================================

set -euo pipefail

WORKFLOW="ci.yml"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
cd "$REPO_ROOT"

# --- pretty logging (TTY-aware) ---------------------------------------------
if [ -t 1 ]; then
  C_RED=$'\033[31m'; C_GRN=$'\033[32m'; C_YLW=$'\033[33m'; C_BLU=$'\033[34m'; C_RST=$'\033[0m'
else
  C_RED=''; C_GRN=''; C_YLW=''; C_BLU=''; C_RST=''
fi
log()  { printf '%s[gh-ci]%s %s\n' "$C_BLU" "$C_RST" "$*"; }
ok()   { printf '%s[ ok ]%s %s\n'  "$C_GRN" "$C_RST" "$*"; }
warn() { printf '%s[warn]%s %s\n'  "$C_YLW" "$C_RST" "$*" >&2; }
err()  { printf '%s[FAIL]%s %s\n'  "$C_RED" "$C_RST" "$*" >&2; }

# --- preflight: gh present + authenticated ----------------------------------
require_gh() {
  if ! command -v gh >/dev/null 2>&1; then
    err "GitHub CLI 'gh' is not installed in this sandbox."
    exit 3
  fi
  if ! gh auth status >/dev/null 2>&1; then
    err "gh is not authenticated. Run: gh auth login"
    exit 3
  fi
}

# Resolve the latest CI run id (any status) if the caller didn't pass one.
latest_run_id() {
  gh run list --workflow "$WORKFLOW" --limit 1 --json databaseId \
    --jq '.[0].databaseId'
}

# Resolve "given arg or latest".
resolve_run() {
  local rid="${1:-}"
  if [ -n "$rid" ]; then echo "$rid"; return; fi
  rid="$(latest_run_id || true)"
  if [ -z "$rid" ] || [ "$rid" = "null" ]; then
    err "No CI runs found for $WORKFLOW yet. Trigger one: gh-ci.sh build"
    exit 4
  fi
  echo "$rid"
}

# ---------------------------------------------------------------------------
cmd_build() {
  local scope="all" config="Debug" os="linux" filter="" postgres="true"
  local ref="" wait="1"
  while [ $# -gt 0 ]; do
    case "$1" in
      --scope)   shift; scope="${1:-}";;
      --config)  shift; config="${1:-}";;
      --os)      shift; os="${1:-}";;
      --filter)  shift; filter="${1:-}";;
      --no-postgres) postgres="false";;
      --ref)     shift; ref="${1:-}";;
      --no-wait) wait="0";;
      -h|--help) sed -n '2,60p' "${BASH_SOURCE[0]}"; exit 0;;
      *) err "Unknown build option: $1"; exit 2;;
    esac
    shift
  done

  [ -z "$ref" ] && ref="$(git rev-parse --abbrev-ref HEAD 2>/dev/null || echo main)"

  log "Triggering $WORKFLOW on ref=$ref  scope=$scope config=$config os=$os postgres=$postgres filter='${filter:-<none>}'"

  gh workflow run "$WORKFLOW" \
    --ref "$ref" \
    -f scope="$scope" \
    -f configuration="$config" \
    -f os="$os" \
    -f filter="$filter" \
    -f with_postgres="$postgres"

  # `gh workflow run` doesn't return the run id; poll briefly for the new run.
  log "Waiting for the run to register ..."
  local rid="" i=0
  while [ "$i" -lt 20 ]; do
    rid="$(gh run list --workflow "$WORKFLOW" --branch "$ref" --limit 1 \
            --json databaseId,status --jq '.[0].databaseId' 2>/dev/null || true)"
    [ -n "$rid" ] && [ "$rid" != "null" ] && break
    sleep 3; i=$((i+1))
  done
  if [ -z "$rid" ] || [ "$rid" = "null" ]; then
    warn "Could not resolve the new run id automatically. Use: gh-ci.sh list"
    exit 0
  fi
  ok "Run started: #$rid  ($(gh run view "$rid" --json url --jq .url))"

  if [ "$wait" = "0" ]; then
    log "--no-wait: returning now. Inspect later with: gh-ci.sh watch $rid"
    return 0
  fi

  log "Streaming run #$rid until it completes ..."
  # `gh run watch --exit-status` returns non-zero if the run failed.
  set +e
  gh run watch "$rid" --exit-status --interval 10
  local rc=$?
  set -e
  echo
  if [ "$rc" -eq 0 ]; then
    ok "CI PASSED (run #$rid)"
  else
    err "CI FAILED (run #$rid). Failing step logs:"
    gh run view "$rid" --log-failed || true
    echo
    log "Download test results with: scripts/sandbox/gh-ci.sh results $rid"
  fi
  return "$rc"
}

cmd_watch()  { local r; r="$(resolve_run "${1:-}")"; log "watching #$r"; gh run watch "$r" --exit-status --interval 10; }
cmd_logs()   { local r; r="$(resolve_run "${1:-}")"; gh run view "$r" --log; }
cmd_failed() { local r; r="$(resolve_run "${1:-}")"; gh run view "$r" --log-failed; }
cmd_status() { local r; r="$(resolve_run "${1:-}")"; gh run view "$r" --json databaseId,status,conclusion,displayTitle,headBranch,url --jq '"#\(.databaseId) \(.status)/\(.conclusion // "-") [\(.headBranch)] \(.displayTitle)\n\(.url)"'; }
cmd_open()   { local r; r="$(resolve_run "${1:-}")"; gh run view "$r" --json url --jq .url; }
cmd_rerun()  { local r; r="$(resolve_run "${1:-}")"; log "re-running failed jobs of #$r"; gh run rerun "$r" --failed; }
cmd_cancel() { local r; r="$(resolve_run "${1:-}")"; log "cancelling #$r"; gh run cancel "$r"; }

cmd_list() {
  gh run list --workflow "$WORKFLOW" --limit "${1:-15}" \
    --json databaseId,status,conclusion,headBranch,displayTitle,createdAt \
    --jq '.[] | "#\(.databaseId)  \(.status)/\(.conclusion // "-")  [\(.headBranch)]  \(.createdAt)  \(.displayTitle)"'
}

# Download the .trx artifacts of a run and print a pass/fail/skip summary.
cmd_results() {
  local r; r="$(resolve_run "${1:-}")"
  local out; out="$REPO_ROOT/.sandbox/ci-results/$r"
  rm -rf "$out"; mkdir -p "$out"
  log "Downloading test-results artifacts of run #$r -> $out"
  if ! gh run download "$r" --pattern 'test-results-*' --dir "$out" 2>/dev/null; then
    warn "No test-results-* artifacts on run #$r (build-only/restore scope, or the build failed before tests)."
    log "Failing step logs:"
    gh run view "$r" --log-failed || true
    return 0
  fi

  # Summarise every .trx with a tiny Python parser (no extra deps).
  python3 - "$out" <<'PY'
import glob, os, sys, xml.etree.ElementTree as ET
root = sys.argv[1]
ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
trx = sorted(glob.glob(os.path.join(root, "**", "*.trx"), recursive=True))
if not trx:
    print("  (no .trx files found)")
    sys.exit(0)
grand = {"total":0,"passed":0,"failed":0,"skipped":0}
fails = []
for f in trx:
    try:
        c = ET.parse(f).getroot().find("t:ResultSummary/t:Counters", ns)
    except Exception as e:
        print(f"  ! could not parse {f}: {e}"); continue
    if c is None:  # fall back to counting UnitTestResult outcomes
        res = ET.parse(f).getroot().findall(".//t:UnitTestResult", ns)
        total=len(res); failed=sum(1 for x in res if x.get("outcome")=="Failed")
        passed=sum(1 for x in res if x.get("outcome")=="Passed")
        skipped=total-passed-failed
    else:
        total=int(c.get("total",0)); passed=int(c.get("passed",0))
        failed=int(c.get("failed",0))
        skipped=total-passed-failed
    name=os.path.basename(f)
    mark = "FAIL" if failed else "ok"
    print(f"  [{mark:>4}] {name:40s} total={total:<4} passed={passed:<4} failed={failed:<4} skipped={skipped}")
    for k,v in (("total",total),("passed",passed),("failed",failed),("skipped",skipped)):
        grand[k]+=v
    if failed:
        for x in ET.parse(f).getroot().findall(".//t:UnitTestResult", ns):
            if x.get("outcome")=="Failed":
                msg=x.find("t:Output/t:ErrorInfo/t:Message", ns)
                fails.append((x.get("testName","?"), (msg.text or "").strip().splitlines()[0] if msg is not None and msg.text else ""))
print("  " + "-"*72)
print(f"  TOTAL  total={grand['total']} passed={grand['passed']} failed={grand['failed']} skipped={grand['skipped']}")
if fails:
    print("\n  Failed tests:")
    for name,msg in fails[:40]:
        print(f"   - {name}\n       {msg}")
    if len(fails)>40:
        print(f"   ... and {len(fails)-40} more")
sys.exit(1 if grand["failed"] else 0)
PY
}

# ---------------------------------------------------------------------------
require_gh
CMD="${1:-help}"; shift || true
case "$CMD" in
  build)   cmd_build "$@";;
  watch)   cmd_watch "$@";;
  logs)    cmd_logs "$@";;
  failed)  cmd_failed "$@";;
  results) cmd_results "$@";;
  list)    cmd_list "$@";;
  status)  cmd_status "$@";;
  rerun)   cmd_rerun "$@";;
  cancel)  cmd_cancel "$@";;
  open)    cmd_open "$@";;
  help|-h|--help) sed -n '2,78p' "${BASH_SOURCE[0]}";;
  *) err "Unknown command: $CMD"; echo; sed -n '2,78p' "${BASH_SOURCE[0]}"; exit 2;;
esac
