#!/usr/bin/env bash
# =============================================================================
# gh-ci.sh  —  one-command driver for Animora CI from the Genspark sandbox
# =============================================================================
# WHY THIS EXISTS
#   In the Genspark sandbox the GitHub App token can PUSH commits but is NOT
#   allowed to call the workflow_dispatch / re-run REST APIs — they return
#   HTTP 403 "Resource not accessible by integration". So the normal
#   `gh workflow run animora-ci.yml -f scope=...` path simply does not work
#   here, and neither does `gh run rerun`.
#
#   This script gives the model the SAME control it would have had from
#   workflow_dispatch, but over the one channel that IS allowed: a git push.
#   It writes the requested knobs into `ci.params`, commits, and pushes to a
#   throwaway `ci/<name>` branch. The workflow's `push: [ci/**]` trigger fires,
#   the Plan job reads ci.params, and the run executes exactly that scenario.
#   The script then (optionally) watches the run and prints the result + any
#   failing-job logs, so the model gets a single, synchronous answer.
#
# ZERO plumbing for the caller: no manual git add/commit/push, no branch
# juggling, no API calls. Just describe what you want.
#
# -----------------------------------------------------------------------------
# USAGE
#   scripts/ci/gh-ci.sh [options]
#
# OPTIONS (all optional; they map 1:1 to the workflow_dispatch inputs)
#   --scope        all|server|desktop|core     (default: all)
#   --action       build-and-test|build-only|test-only  (default: build-and-test)
#   --config       Debug|Release               (default: Debug)
#   --filter       <text>   run only tests whose FQN contains <text>
#   --postgres     true|false  run server tests against a real PostgreSQL (default: true)
#   --publish      true|false  also publish a downloadable Windows desktop build (default: false)
#   --runtime      win-x64|win-arm64           (default: win-x64)
#
#   --branch <name>   ci branch to push to      (default: ci/sandbox)
#                     Always force-pushed; it is a scratch lane, not history.
#   --no-watch        push and return immediately (don't wait for the run).
#   --watch-timeout <sec>  how long to wait for the run to appear/finish (default: 1800)
#   --repo <owner/name>    override the repo (default: inferred from `origin`)
#   --cleanup         delete the ci/<name> branch after the run finishes, so
#                     scratch branches don't pile up (ignored with --no-watch).
#   --dry-run         show the ci.params that would be pushed, then stop.
#   -h | --help       show this help.
#
# EXAMPLES
#   # Build + test everything against a real PostgreSQL, wait, print result:
#   scripts/ci/gh-ci.sh
#
#   # Only the server track, only tests, only the Sync.* cases:
#   scripts/ci/gh-ci.sh --scope server --action test-only --filter Sync
#
#   # Release build of the desktop app + a downloadable self-contained build:
#   scripts/ci/gh-ci.sh --scope desktop --action build-only --config Release \
#       --publish true --runtime win-x64
#
#   # Fire and forget (inspect later with: gh run list / gh run view --log):
#   scripts/ci/gh-ci.sh --scope core --no-watch
#
# REQUIREMENTS
#   * `gh` authenticated (gh auth status) — already true in the sandbox.
#   * A clean-ish working tree is NOT required: this script stashes nothing and
#     commits ONLY ci.params onto the ci branch, leaving your other work alone.
# =============================================================================

set -uo pipefail

# --- defaults ----------------------------------------------------------------
SCOPE="all"
ACTION="build-and-test"
CONFIG="Debug"
FILTER=""
POSTGRES="true"
PUBLISH="false"
RUNTIME="win-x64"
BRANCH="ci/sandbox"
WATCH="true"
WATCH_TIMEOUT="1800"
REPO=""
DRY_RUN="false"
CLEANUP="false"

# --- logging -----------------------------------------------------------------
if [ -t 1 ]; then C_RED=$'\033[31m'; C_GRN=$'\033[32m'; C_YLW=$'\033[33m'; C_BLU=$'\033[34m'; C_RST=$'\033[0m'
else C_RED=''; C_GRN=''; C_YLW=''; C_BLU=''; C_RST=''; fi
log()  { printf '%s[gh-ci]%s %s\n' "$C_BLU" "$C_RST" "$*"; }
ok()   { printf '%s[gh-ci  ok]%s %s\n' "$C_GRN" "$C_RST" "$*"; }
warn() { printf '%s[gh-ci warn]%s %s\n' "$C_YLW" "$C_RST" "$*" >&2; }
die()  { printf '%s[gh-ci FAIL]%s %s\n' "$C_RED" "$C_RST" "$*" >&2; exit 1; }

usage() { sed -n '2,70p' "$0" | sed 's/^# \{0,1\}//'; }

# --- parse args --------------------------------------------------------------
while [ $# -gt 0 ]; do
  case "$1" in
    --scope)         SCOPE="${2:?}"; shift 2 ;;
    --action)        ACTION="${2:?}"; shift 2 ;;
    --config)        CONFIG="${2:?}"; shift 2 ;;
    --filter)        FILTER="${2:-}"; shift 2 ;;
    --postgres)      POSTGRES="${2:?}"; shift 2 ;;
    --publish)       PUBLISH="${2:?}"; shift 2 ;;
    --runtime)       RUNTIME="${2:?}"; shift 2 ;;
    --branch)        BRANCH="${2:?}"; shift 2 ;;
    --no-watch)      WATCH="false"; shift ;;
    --watch-timeout) WATCH_TIMEOUT="${2:?}"; shift 2 ;;
    --repo)          REPO="${2:?}"; shift 2 ;;
    --cleanup)       CLEANUP="true"; shift ;;
    --dry-run)       DRY_RUN="true"; shift ;;
    -h|--help)       usage; exit 0 ;;
    *) die "unknown option: $1 (try --help)" ;;
  esac
done

# --- light validation (the workflow validates authoritatively too) ----------
case "$SCOPE"   in all|server|desktop|core) ;; *) die "invalid --scope '$SCOPE'" ;; esac
case "$ACTION"  in build-and-test|build-only|test-only) ;; *) die "invalid --action '$ACTION'" ;; esac
case "$CONFIG"  in Debug|Release) ;; *) die "invalid --config '$CONFIG'" ;; esac
case "$RUNTIME" in win-x64|win-arm64) ;; *) die "invalid --runtime '$RUNTIME'" ;; esac
case "$BRANCH"  in ci/*) ;; *) die "--branch must start with 'ci/' (got '$BRANCH')" ;; esac

command -v gh  >/dev/null 2>&1 || die "'gh' CLI not found."
command -v git >/dev/null 2>&1 || die "'git' not found."
gh auth status >/dev/null 2>&1 || die "'gh' is not authenticated (run: gh auth status)."

REPO_ROOT="$(git rev-parse --show-toplevel 2>/dev/null)" || die "not inside a git repo."
cd "$REPO_ROOT"

if [ -z "$REPO" ]; then
  # owner/name from origin (https or ssh).
  ORIGIN="$(git remote get-url origin 2>/dev/null || true)"
  REPO="$(printf '%s' "$ORIGIN" | sed -E 's#(git@github.com:|https://github.com/)##; s#\.git$##')"
  [ -n "$REPO" ] || die "could not infer repo from origin; pass --repo owner/name."
fi

# --- compose ci.params content ----------------------------------------------
STAMP="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
read -r -d '' PARAMS <<EOF || true
# Animora CI control file — generated by scripts/ci/gh-ci.sh at $STAMP
# The Plan job in animora-ci.yml reads these KEY=VALUE lines on a ci/** push.
# Edit + push this file (or just re-run gh-ci.sh) to launch a scoped run.
scope=$SCOPE
action=$ACTION
configuration=$CONFIG
test_filter=$FILTER
server_tests_with_postgres=$POSTGRES
publish_desktop=$PUBLISH
desktop_runtime=$RUNTIME
EOF

log "repo=$REPO  branch=$BRANCH"
log "plan -> scope=$SCOPE action=$ACTION config=$CONFIG filter='${FILTER:-}' postgres=$POSTGRES publish=$PUBLISH runtime=$RUNTIME"

if [ "$DRY_RUN" = "true" ]; then
  echo "----- ci.params (dry-run, not pushed) -----"
  printf '%s\n' "$PARAMS"
  echo "-------------------------------------------"
  exit 0
fi

# --- build a control commit WITHOUT touching the caller's branch/worktree -----
# Everything below uses git plumbing (a temporary index + commit-tree). The
# checked-out branch, HEAD, and working tree are never modified, so the model's
# in-progress work in the sandbox is completely safe.
START_SHA="$(git rev-parse HEAD)" || die "could not resolve HEAD."

# Build the new tree = HEAD's tree + our ci.params, via a throwaway index file.
TMP_INDEX="$(mktemp)"
TMP_PARAMS="$(mktemp)"
printf '%s\n' "$PARAMS" > "$TMP_PARAMS"
TREE="$(
  GIT_INDEX_FILE="$TMP_INDEX" sh -c '
    set -e
    git read-tree "$1"
    # Inject ci.params as a blob at the repo root of the temp index.
    BLOB="$(git hash-object -w "$2")"
    git update-index --add --cacheinfo 100644 "$BLOB" ci.params
    git write-tree
  ' _ "$START_SHA" "$TMP_PARAMS"
)" || true
rm -f "$TMP_INDEX" "$TMP_PARAMS"
[ -n "$TREE" ] || die "could not stage ci.params into a temporary tree."

COMMIT_MSG="ci: run scope=$SCOPE action=$ACTION config=$CONFIG${FILTER:+ filter=$FILTER} (gh-ci.sh)"
NEW_COMMIT="$(git commit-tree "$TREE" -p "$START_SHA" -m "$COMMIT_MSG")"
[ -n "$NEW_COMMIT" ] || die "could not create the control commit."

log "pushing control commit to $BRANCH (force) ..."
if ! git push -f origin "$NEW_COMMIT:refs/heads/$BRANCH" 2>&1; then
  die "git push failed. Check 'gh auth status' and that you have push access to $REPO."
fi
ok "pushed $NEW_COMMIT -> $BRANCH. The push trigger will start a run."

if [ "$WATCH" != "true" ]; then
  log "watching disabled (--no-watch). Inspect later with:"
  log "  gh run list  --repo $REPO --branch $BRANCH"
  log "  gh run view  --repo $REPO --log     # or --log-failed"
  exit 0
fi

# --- find the run started by THIS push, then watch it ------------------------
log "waiting for the run to appear (timeout ${WATCH_TIMEOUT}s) ..."
RUN_ID=""
DEADLINE=$(( $(date +%s) + WATCH_TIMEOUT ))
while [ -z "$RUN_ID" ] && [ "$(date +%s)" -lt "$DEADLINE" ]; do
  RUN_ID="$(gh run list --repo "$REPO" --branch "$BRANCH" \
             --workflow "Animora CI" --limit 1 \
             --json databaseId,headSha \
             --jq ".[] | select(.headSha==\"$NEW_COMMIT\") | .databaseId" 2>/dev/null || true)"
  [ -z "$RUN_ID" ] && sleep 5
done
[ -n "$RUN_ID" ] || { warn "run did not appear in time; check Actions tab or gh run list."; exit 1; }

log "watching run $RUN_ID ..."
gh run watch "$RUN_ID" --repo "$REPO" --exit-status --interval 10 >/dev/null 2>&1
RUN_RC=$?

maybe_cleanup() {
  [ "$CLEANUP" = "true" ] || return 0
  log "cleaning up scratch branch $BRANCH ..."
  git push origin --delete "$BRANCH" >/dev/null 2>&1 \
    && ok "deleted $BRANCH" \
    || warn "could not delete $BRANCH (delete it by hand if needed)."
}

echo
log "================= RESULT ================="
gh run view "$RUN_ID" --repo "$REPO" 2>&1 | sed 's/^/  /'
echo

if [ "$RUN_RC" -ne 0 ]; then
  warn "run finished with FAILURES — failing-job logs below:"
  echo "----------------------------------------------------"
  gh run view "$RUN_ID" --repo "$REPO" --log-failed 2>&1 | tail -n 200
  echo "----------------------------------------------------"
  warn "full logs:      gh run view $RUN_ID --repo $REPO --log"
  warn "TRX artifacts:  gh run download $RUN_ID --repo $REPO"
  maybe_cleanup
  exit "$RUN_RC"
fi

ok "CI SUCCEEDED (run $RUN_ID)."
log "artifacts (if any): gh run download $RUN_ID --repo $REPO"
maybe_cleanup
exit 0
