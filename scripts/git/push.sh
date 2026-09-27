#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# scripts/git/push.sh — one-shot: stage ALL → commit → sync (rebase) → push
#
# USAGE (from anywhere inside the repo):
#   scripts/git/push.sh "type(scope): message"      # commit + push
#   scripts/git/push.sh                             # auto message from diff
#   scripts/git/push.sh -b <branch> "message"       # switch/create branch first
#
# EXIT CODES: 0 ok | 1 generic | 2 rebase conflict (repo left clean, re-run
#             after resolving) | 3 no credentials (call setup_github_environment
#             tool, then re-run)
# ─────────────────────────────────────────────────────────────────────────────
set -uo pipefail

REPO="$(git rev-parse --show-toplevel 2>/dev/null)" || { echo "✗ not a git repo"; exit 1; }
cd "$REPO"

BRANCH=""
while [ $# -gt 0 ]; do
  case "$1" in
    -b|--branch) BRANCH="$2"; shift 2 ;;
    -h|--help) sed -n '2,13p' "$0"; exit 0 ;;
    *) break ;;
  esac
done
MSG="${*:-}"

# ── identity (fresh sessions may have none) ──────────────────────────────────
git config user.name  >/dev/null || git config user.name  "AI Developer"
git config user.email >/dev/null || git config user.email "ai-developer@users.noreply.github.com"
git config pull.rebase true >/dev/null
git config rebase.autoStash true >/dev/null

# ── branch ───────────────────────────────────────────────────────────────────
if [ -n "$BRANCH" ]; then
  git switch -q "$BRANCH" 2>/dev/null || git switch -q -c "$BRANCH" || exit 1
fi
CUR="$(git symbolic-ref --short -q HEAD)" || { echo "✗ detached HEAD; use -b <branch>"; exit 1; }

# ── stage + commit ───────────────────────────────────────────────────────────
git add -A
if ! git diff --cached --quiet; then
  if [ -z "$MSG" ]; then
    # auto message: "chore: update N files (a, b, c)"
    FILES=$(git diff --cached --name-only)
    N=$(printf '%s\n' "$FILES" | wc -l | tr -d ' ')
    LIST=$(printf '%s\n' "$FILES" | head -3 | xargs -n1 basename | paste -sd, - | sed 's/,/, /g')
    [ "$N" -gt 3 ] && LIST="$LIST, +$((N-3)) more"
    MSG="chore: update $N file(s) ($LIST)"
  fi
  git commit -q -m "$MSG" || exit 1
  COMMITTED=1
else
  COMMITTED=0
fi

# ── sync with remote (rebase, autostash) ─────────────────────────────────────
REMOTE="$(git config --get "branch.$CUR.remote" || echo origin)"
if git fetch -q "$REMOTE" "$CUR" 2>/dev/null; then
  if ! git rebase -q "$REMOTE/$CUR" 2>/dev/null; then
    git rebase --abort 2>/dev/null
    echo "✗ conflict rebasing onto $REMOTE/$CUR — resolve manually then re-run"
    exit 2
  fi
fi

# ── push ─────────────────────────────────────────────────────────────────────
OUT="$(git push -q -u "$REMOTE" "$CUR" 2>&1)"; RC=$?
if [ $RC -ne 0 ]; then
  if printf '%s' "$OUT" | grep -qiE 'authentication|could not read|403|401|denied|credential'; then
    echo "✗ no GitHub credentials in this session → run the setup_github_environment tool, then re-run this script"
    exit 3
  fi
  echo "✗ push failed:"; echo "$OUT"; exit 1
fi

SHA="$(git rev-parse --short HEAD)"
URL="$(git remote get-url "$REMOTE" | sed -E 's#^git@github\.com:#https://github.com/#; s#\.git$##')"
if [ $COMMITTED -eq 1 ]; then
  echo "✓ $CUR@$SHA pushed — \"$MSG\" — $URL/commit/$SHA"
else
  echo "✓ $CUR@$SHA up to date on $REMOTE (nothing new to commit)"
fi
