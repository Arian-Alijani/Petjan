#!/usr/bin/env bash
# =============================================================================
# restore-drill.sh — rehearsed restore (G07, ARCH §17.3 / §19 gate R + K).
#
# One consistency boundary = THREE parts captured as one point-in-time set:
#   (1) server database (pg_dump, key-ring tables excluded)
#   (2) the file-content tree (tar of DataRootPath)
#   (3) the Data-Protection key ring (pg_dump of DataProtectionKeys +
#       __EFMigrationsHistory_KeyRing — the key-ring context's own tables)
#
# Drill:
#   a) migrate schema as OWNER (dotnet-ef, both contexts); seed 1 tenant + 1 key + 1 stored file
#   b) snapshot the three parts          c) restore onto a CLEAN database + dir
#   d) start the server -> /health/ready == 200, tenant row + file intact
#   e) record elapsed restore seconds -> artifacts/restore-rto.txt
#   f) key-ring-missing variant: restore DB+files but NOT the key ring ->
#      /health/live == 200, /health/ready == 503 (A-RULE 027, ARCH §10.6)
#
# Env:
#   ANIMORA_PG_TEST_CONNECTION   admin Npgsql connection string (CREATE DATABASE capable). Unset -> skip.
#   ANIMORA_PG_OWNER_CONNECTION  optional DDL/owner connection (defaults to the admin one).
#   ANIMORA_DRILL_PORT           server port (default 5199).
# =============================================================================
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
cd "$REPO_ROOT"

ADMIN_CONN="${ANIMORA_PG_TEST_CONNECTION:-}"
if [ -z "$ADMIN_CONN" ]; then
  echo "restore-drill: ANIMORA_PG_TEST_CONNECTION is not set — skipping (nothing executed)"
  exit 0
fi
OWNER_CONN="${ANIMORA_PG_OWNER_CONNECTION:-$ADMIN_CONN}"
PORT="${ANIMORA_DRILL_PORT:-5199}"
ART="$REPO_ROOT/artifacts"; mkdir -p "$ART"
WORK="$(mktemp -d "${TMPDIR:-/tmp}/animora-drill.XXXXXX")"
SUFFIX="$(date +%s)_$$"
DB_SRC="drill_src_$SUFFIX"; DB_DST="drill_dst_$SUFFIX"; DB_NOKEY="drill_nokey_$SUFFIX"
SERVER_PID=""

# ── helpers ──────────────────────────────────────────────────────────────────
kv() { # kv <conn> <Key>  -> value from an Npgsql "A=b;C=d" string (case-insensitive)
  printf '%s' "$1" | tr ';' '\n' | awk -F= -v k="$(printf '%s' "$2" | tr '[:upper:]' '[:lower:]')" \
    'tolower($1)==k {sub(/^[^=]*=/,""); print; exit}'
}
export_pg() { # export_pg <conn> <db>
  export PGHOST="$(kv "$1" Host)" PGPORT="$(kv "$1" Port)" PGUSER="$(kv "$1" Username)" PGPASSWORD="$(kv "$1" Password)"
  : "${PGPORT:=5432}"; export PGPORT; export PGDATABASE="$2"
}
conn_for() { # conn_for <base-conn> <db>  -> connection string pointing at <db>
  printf '%s' "$1" | tr ';' '\n' | grep -vi '^database=' | paste -sd';' - | sed "s/\$/;Database=$2/"
}
psql_q() { psql -v ON_ERROR_STOP=1 -qtAX "$@"; }
step() { printf '── restore-drill: %s\n' "$*"; }
stop_server() {
  if [ -n "$SERVER_PID" ] && kill -0 "$SERVER_PID" 2>/dev/null; then
    kill "$SERVER_PID" 2>/dev/null || true; wait "$SERVER_PID" 2>/dev/null || true
  fi
  SERVER_PID=""
}
cleanup() {
  stop_server
  export_pg "$ADMIN_CONN" postgres
  for d in "$DB_SRC" "$DB_DST" "$DB_NOKEY"; do psql_q -c "DROP DATABASE IF EXISTS \"$d\" WITH (FORCE)" >/dev/null 2>&1 || true; done
  rm -rf "$WORK"
}
trap cleanup EXIT

EF="$REPO_ROOT/.sandbox/tools/dotnet-ef"; [ -x "$EF" ] || EF="dotnet ef"
migrate_as_owner() { # migrate_as_owner <db>
  local conn; conn="$(conn_for "$OWNER_CONN" "$1")"
  for ctx in AnimoraServerDbContext AnimoraKeyRingDbContext; do
    ANIMORA_PG_DESIGN_CONNECTION="$conn" $EF database update --context "$ctx" \
      --project src/Animora.Persistence.PostgreSql --startup-project src/Animora.Persistence.PostgreSql >/dev/null
  done
}

# Starts the server against <db>/<dataroot>; waits for liveness. Sets SERVER_PID.
start_server() { # start_server <db> <dataroot>
  stop_server
  ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS="http://127.0.0.1:$PORT" \
  Database__ConnectionString="$(conn_for "$ADMIN_CONN" "$1")" Server__DataRootPath="$2" \
    dotnet run --project src/Animora.Server --no-launch-profile --no-build >"$WORK/server-$1.log" 2>&1 &
  SERVER_PID=$!
  for _ in $(seq 1 60); do
    curl -fsS "http://127.0.0.1:$PORT/health/live" >/dev/null 2>&1 && return 0
    kill -0 "$SERVER_PID" 2>/dev/null || { echo "✗ server exited early:"; tail -30 "$WORK/server-$1.log"; return 1; }
    sleep 1
  done
  echo "✗ server never became live"; tail -30 "$WORK/server-$1.log"; return 1
}
http_code() { curl -s -o /dev/null -w '%{http_code}' "http://127.0.0.1:$PORT$1" || true; }
wait_ready_code() { # wait_ready_code <expected> -> echoes final code (polls ≤30s)
  local code=""
  for _ in $(seq 1 30); do code="$(http_code /health/ready)"; [ "$code" = "$1" ] && break; sleep 1; done
  echo "$code"
}

# ── (a) provision source ─────────────────────────────────────────────────────
step "build server (once, Debug)"
dotnet build src/Animora.Server/Animora.Server.csproj -c Debug -v quiet --nologo >/dev/null

step "(a) provision source database as OWNER"
export_pg "$ADMIN_CONN" postgres
psql_q -c "CREATE DATABASE \"$DB_SRC\"" >/dev/null
migrate_as_owner "$DB_SRC"
export_pg "$ADMIN_CONN" "$DB_SRC"
psql_q -c "INSERT INTO public.\"Tenants\" (\"Id\",\"TenantId\",\"DisplayName\",\"Timezone\",\"DefaultCurrency\",\"Status\",\"UpdatedAt\") VALUES ('DRILL-T1','DRILL-T1','Drill Clinic','Asia/Tehran','IRT',0,now())" >/dev/null
psql_q -c "INSERT INTO public.\"DataProtectionKeys\" (\"FriendlyName\",\"Xml\") VALUES ('drill-key','<key/>')" >/dev/null
SRC_ROOT="$WORK/src-data"; mkdir -p "$SRC_ROOT/files/DRILL-T1"
printf 'drill-file-content-%s' "$SUFFIX" >"$SRC_ROOT/files/DRILL-T1/known.bin"
EXPECT_SUM="$(sha256sum "$SRC_ROOT/files/DRILL-T1/known.bin" | cut -d' ' -f1)"

# ── (b) one point-in-time set of THREE parts ─────────────────────────────────
step "(b) snapshot DB + file tree + key ring"
KR_TABLES=(-t 'public."DataProtectionKeys"' -t 'public."__EFMigrationsHistory_KeyRing"')
pg_dump --no-owner --format=custom --exclude-table='public."DataProtectionKeys"' \
        --exclude-table='public."__EFMigrationsHistory_KeyRing"' -f "$WORK/server.dump"
pg_dump --no-owner --format=custom "${KR_TABLES[@]}" -f "$WORK/keyring.dump"
tar -C "$SRC_ROOT" -czf "$WORK/files.tar.gz" .

# ── (c)+(d)+(e) full restore, timed ──────────────────────────────────────────
step "(c) restore onto CLEAN database + directory"
T0=$(date +%s.%N)
export_pg "$ADMIN_CONN" postgres
psql_q -c "CREATE DATABASE \"$DB_DST\"" >/dev/null
export_pg "$ADMIN_CONN" "$DB_DST"
pg_restore --no-owner -d "$DB_DST" "$WORK/server.dump"
pg_restore --no-owner -d "$DB_DST" "$WORK/keyring.dump"
DST_ROOT="$WORK/dst-data"; mkdir -p "$DST_ROOT"; tar -C "$DST_ROOT" -xzf "$WORK/files.tar.gz"

step "(d) start server on the restored set; assert readiness + data intact"
start_server "$DB_DST" "$DST_ROOT"
CODE="$(wait_ready_code 200)"
T1=$(date +%s.%N)
[ "$CODE" = "200" ] || { echo "✗ restored set not ready (HTTP $CODE)"; tail -30 "$WORK/server-$DB_DST.log"; exit 1; }
ROWS="$(psql_q -c "SELECT count(*) FROM public.\"Tenants\" WHERE \"Id\"='DRILL-T1' AND \"DisplayName\"='Drill Clinic'")"
KEYS="$(psql_q -c "SELECT count(*) FROM public.\"DataProtectionKeys\" WHERE \"FriendlyName\"='drill-key'")"
GOT_SUM="$(sha256sum "$DST_ROOT/files/DRILL-T1/known.bin" | cut -d' ' -f1)"
[ "$ROWS" = "1" ] || { echo "✗ tenant row lost after restore"; exit 1; }
[ "$KEYS" = "1" ] || { echo "✗ key-ring key lost after restore"; exit 1; }
[ "$GOT_SUM" = "$EXPECT_SUM" ] || { echo "✗ stored file differs after restore"; exit 1; }
stop_server

# (e) numeric elapsed seconds (restore + time-to-ready)
awk -v a="$T0" -v b="$T1" 'BEGIN{printf "%.1f\n", b-a}' >"$ART/restore-rto.txt"
echo "✓ restore ok — RTO $(cat "$ART/restore-rto.txt")s (artifacts/restore-rto.txt)"

# ── (f) key-ring-missing variant ─────────────────────────────────────────────
step "(f) restore DB+files WITHOUT the key ring -> liveness 200, readiness 503"
export_pg "$ADMIN_CONN" postgres
psql_q -c "CREATE DATABASE \"$DB_NOKEY\"" >/dev/null
export_pg "$ADMIN_CONN" "$DB_NOKEY"
pg_restore --no-owner -d "$DB_NOKEY" "$WORK/server.dump"          # NOTE: keyring.dump deliberately NOT restored
start_server "$DB_NOKEY" "$DST_ROOT"
LIVE="$(http_code /health/live)"; READY="$(wait_ready_code 503)"
[ "$LIVE" = "200" ]  || { echo "✗ liveness must stay 200 without key ring (got $LIVE)"; exit 1; }
[ "$READY" = "503" ] || { echo "✗ readiness must be 503 without key ring (got $READY)"; exit 1; }
BIZ="$(curl -s -o /dev/null -w '%{http_code}' -X POST "http://127.0.0.1:$PORT/api/commands" -H 'Content-Type: application/json' -d '{}' || true)"
case "$BIZ" in 401|403|503) ;; *) echo "✗ business endpoint must be refused without key ring (got $BIZ)"; exit 1 ;; esac
echo "✓ key-ring-missing: live=200 ready=503 business=$BIZ (refused)"
echo "✓ restore drill PASSED"
