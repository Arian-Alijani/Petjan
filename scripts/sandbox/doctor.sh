#!/usr/bin/env bash
# =============================================================================
# doctor.sh  —  one-shot health/constraints report for this sandbox
# =============================================================================
# Prints, in a compact form, everything the AI agent needs to decide what to do
# next: is the SDK present? is xvfb present? how much RAM/disk/tmpfs is free?
# Never fails the shell — purely informational.
#
#   scripts/sandbox/doctor.sh
# =============================================================================

source "$(dirname "${BASH_SOURCE[0]}")/_env.sh"

echo "===== Animora sandbox doctor ====="
printf '%-14s %s\n' "repo:"    "$REPO_ROOT"
printf '%-14s %s\n' "os:"      "$(. /etc/os-release; echo "$PRETTY_NAME") $(uname -m)"
printf '%-14s %s\n' "cpu:"     "$(nproc) cores"
printf '%-14s %s\n' "mem:"     "$(free -h | awk '/^Mem:/{print $7" avail / "$2" total"}')"
printf '%-14s %s\n' "disk(/):" "$(df -h / | awk 'NR==2{print $4" avail / "$2}')"
printf '%-14s %s\n' "tmpfs:"   "$(df -h /tmp | awk 'NR==2{print $4" avail / "$2" (KEEP SCRATCH OFF THIS)"}')"

echo "----- toolchain -----"
if has_dotnet; then
  printf '%-14s %s\n' "dotnet:" "$(dotnet --version)  ($DOTNET_ROOT)"
else
  printf '%-14s %s\n' "dotnet:" "MISSING -> run install-prereqs.sh"
fi
command -v Xvfb >/dev/null 2>&1 \
  && printf '%-14s %s\n' "xvfb:" "present (headless run OK)" \
  || printf '%-14s %s\n' "xvfb:" "MISSING (build/test OK, run needs it)"

echo "----- postgres (server/DB tests) -----"
PG_PORT_D="${ANIMORA_PG_PORT:-5433}"
if ls -d /usr/lib/postgresql/*/bin >/dev/null 2>&1 || command -v postgres >/dev/null 2>&1; then
  if PGPASSWORD=animora psql -h 127.0.0.1 -p "$PG_PORT_D" -U animora -d postgres -tAc 'SELECT 1' >/dev/null 2>&1; then
    printf '%-14s %s\n' "postgres:" "UP on 127.0.0.1:$PG_PORT_D (server/DB tests will hit a REAL DB)"
  else
    printf '%-14s %s\n' "postgres:" "installed but DOWN -> run install-postgres.sh"
  fi
else
  printf '%-14s %s\n' "postgres:" "MISSING -> run install-postgres.sh (PG-gated tests will SKIP)"
fi

# G07: strict-lane indicator (informational only — never fails the shell).
if [ "${ANIMORA_REQUIRE_PG:-}" = "1" ]; then
  printf '%-14s %s\n' "require_pg:" "SET (strict lane: a missing/unreachable PostgreSQL FAILS tests instead of skipping)"
else
  printf '%-14s %s\n' "require_pg:" "not set (PG-gated tests skip when no database is configured)"
fi
if [ -n "${ANIMORA_PG_TEST_CONNECTION:-}" ]; then
  printf '%-14s %s\n' "pg_conn_env:" "ANIMORA_PG_TEST_CONNECTION is set"
else
  printf '%-14s %s\n' "pg_conn_env:" "ANIMORA_PG_TEST_CONNECTION is not set"
fi

# G09: release-engineering file presence (informational only — never fails the shell).
echo "----- release gates -----"
for f in pg-gates.yml perf-gates.yml release-gates.yml; do
  if [ -f "$REPO_ROOT/.github/workflows/$f" ]; then
    printf '%-18s %s\n' "$f:" "installed (.github/workflows)"
  elif [ -f "$REPO_ROOT/$f" ]; then
    printf '%-18s %s\n' "$f:" "authored at repo root (copy to .github/workflows/ to activate)"
  else
    printf '%-18s %s\n' "$f:" "MISSING"
  fi
done
if [ -f "$REPO_ROOT/scripts/windows/publish-portable.ps1" ]; then
  printf '%-18s %s\n' "publish-portable:" "present$(grep -q 'EmitManifest' "$REPO_ROOT/scripts/windows/publish-portable.ps1" && echo ' (-EmitManifest supported)')"
else
  printf '%-18s %s\n' "publish-portable:" "MISSING"
fi

echo "----- build output -----"
for c in Debug Release; do
  dll="$REPO_ROOT/$DESKTOP_DLL_REL/$c/net10.0/Animora.Desktop.dll"
  [ -f "$dll" ] && printf '%-14s %s\n' "$c:" "built" || printf '%-14s %s\n' "$c:" "-"
done

echo "----- scratch -----"
printf '%-14s %s\n' "nuget:"  "$NUGET_PACKAGES"
printf '%-14s %s\n' "logs:"   "$LOG_DIR"
[ -d "$LOG_DIR" ] && ls -1 "$LOG_DIR" 2>/dev/null | sed 's/^/               log: /' || true
echo "=================================="
