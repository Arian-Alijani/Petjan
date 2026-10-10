#!/usr/bin/env bash
# =============================================================================
# install-postgres.sh  —  fast, local PostgreSQL for the Animora SERVER track
# =============================================================================
# Stands up a private, throwaway PostgreSQL 17 cluster entirely inside the repo
# so the PostgreSQL-gated server/persistence tests (PgFixture →
# ANIMORA_PG_TEST_CONNECTION) can run against a REAL database in this sandbox.
#
# Why its own cluster (not the system service):
#   * No systemd here, and we must keep the data dir OFF the 493 MiB /tmp tmpfs.
#   * A dedicated cluster under .sandbox/ is disposable, root-free to operate,
#     and tuned for TEST throughput (durability deliberately traded away).
#
# Speed / low-RAM design (this ~985 MiB box):
#   * apt install with --no-install-recommends (server + client only).
#   * initdb trust auth → no password handshakes.
#   * fsync=off, synchronous_commit=off, full_page_writes=off, small buffers:
#     a test DB is recreated per fixture, so durability is worthless and slow.
#   * Idempotent: re-running reuses the installed packages, the existing
#     cluster, and only (re)starts the server if it is down.
#
# Usage:
#   scripts/sandbox/install-postgres.sh          # install + init + start
#   scripts/sandbox/install-postgres.sh status   # is it up? print conn string
#   scripts/sandbox/install-postgres.sh stop     # stop the cluster
#   scripts/sandbox/install-postgres.sh restart  # stop + start
#   scripts/sandbox/install-postgres.sh nuke     # stop + delete the data dir
#
# On success it prints, and writes to .sandbox/pg/connection.env, the admin
# connection string expected by the tests:
#   ANIMORA_PG_TEST_CONNECTION=Host=...;Port=...;Username=...;Password=...
# =============================================================================

source "$(dirname "${BASH_SOURCE[0]}")/_env.sh"

# --- cluster layout (all on the 18 GB root disk, never /tmp) ------------------
PG_HOME="$SCRATCH/pg"
PG_DATA="$PG_HOME/data"
PG_LOG="$LOG_DIR/postgres.log"
PG_PORT="${ANIMORA_PG_PORT:-5433}"          # 5433: avoid clashing with any system :5432
PG_SOCKET_DIR="$PG_HOME/sock"
PG_SUPERUSER="animora"
PG_CONN_FILE="$PG_HOME/connection.env"
mkdir -p "$PG_HOME" "$PG_SOCKET_DIR"

# The admin connection string the tests consume (Npgsql/ADO.NET keyword form;
# role may CREATE/DROP DATABASE).
PG_CONN="Host=127.0.0.1;Port=$PG_PORT;Username=$PG_SUPERUSER;Password=animora;Database=postgres"

# psql speaks libpq keywords, NOT the Npgsql form above — use flags for health checks.
pg_ping() { PGPASSWORD=animora psql -h 127.0.0.1 -p "$PG_PORT" -U "$PG_SUPERUSER" -d postgres -tAc 'SELECT 1' >/dev/null 2>&1; }

# --- locate the PostgreSQL binaries (Debian hides them in /usr/lib/...) -------
find_pg_bin() {
  local b
  for b in pg_ctl initdb postgres psql; do
    if command -v "$b" >/dev/null 2>&1; then continue; fi
    local hit
    hit="$(ls -d /usr/lib/postgresql/*/bin 2>/dev/null | sort -V | tail -1 || true)"
    if [ -n "$hit" ] && [ -x "$hit/$b" ]; then
      export PATH="$hit:$PATH"
    fi
  done
}

# -----------------------------------------------------------------------------
# apt install (only if the server binary is absent)
# -----------------------------------------------------------------------------
install_packages() {
  find_pg_bin
  if command -v initdb >/dev/null 2>&1 && command -v pg_ctl >/dev/null 2>&1; then
    ok "PostgreSQL binaries already present ($(postgres --version 2>/dev/null || echo installed))."
    return 0
  fi

  log "Installing PostgreSQL server (apt, --no-install-recommends) ..."
  if ! (sudo -n true 2>/dev/null || sudo true 2>/dev/null); then
    err "sudo unavailable — cannot apt-install PostgreSQL."
    exit 4
  fi
  export DEBIAN_FRONTEND=noninteractive
  if ! sudo apt-get update -qq; then
    err "apt-get update failed (network?)."
    exit 4
  fi
  # postgresql-17 pulls in the matching -client; recommends skipped for speed.
  if ! sudo apt-get install -y -qq --no-install-recommends postgresql-17 postgresql-client-17; then
    warn "pinned postgresql-17 failed; falling back to meta 'postgresql'."
    if ! sudo apt-get install -y -qq --no-install-recommends postgresql postgresql-client; then
      err "PostgreSQL install failed — see apt output above."
      exit 4
    fi
  fi
  find_pg_bin
  if ! command -v initdb >/dev/null 2>&1; then
    err "initdb still not on PATH after install."
    exit 4
  fi
  ok "PostgreSQL installed: $(postgres --version 2>/dev/null)."
}

# -----------------------------------------------------------------------------
# initdb — create the cluster once (test-tuned, durability OFF)
# -----------------------------------------------------------------------------
init_cluster() {
  if [ -f "$PG_DATA/PG_VERSION" ]; then
    ok "Cluster already initialized at $PG_DATA (skipping initdb)."
    return 0
  fi
  log "Initializing cluster at $PG_DATA (auth=trust, superuser=$PG_SUPERUSER) ..."
  rm -rf "$PG_DATA"; mkdir -p "$PG_DATA"
  # trust auth => no password prompts; still set one so a password-bearing conn
  # string (as the tests use) authenticates cleanly.
  local pwfile="$PG_HOME/.initpw"
  printf 'animora' >"$pwfile"
  if ! initdb -D "$PG_DATA" -U "$PG_SUPERUSER" --auth=trust --pwfile="$pwfile" \
        --encoding=UTF8 --locale=C >>"$PG_LOG" 2>&1; then
    rm -f "$pwfile"
    err "initdb failed — tail of $PG_LOG:"; tail -20 "$PG_LOG" >&2 || true
    exit 5
  fi
  rm -f "$pwfile"

  # --- test-only tuning: trade ALL durability for speed on a tiny box --------
  cat >>"$PG_DATA/postgresql.conf" <<EOF

# ===== Animora sandbox test tuning (durability intentionally OFF) =====
listen_addresses = '127.0.0.1'
port = $PG_PORT
unix_socket_directories = '$PG_SOCKET_DIR'
max_connections = 50
shared_buffers = 64MB
work_mem = 8MB
maintenance_work_mem = 32MB
effective_cache_size = 256MB
# Durability off: each test DB is created then dropped; crash-safety is useless.
fsync = off
synchronous_commit = off
full_page_writes = off
wal_level = minimal
max_wal_senders = 0
checkpoint_timeout = 30min
autovacuum = off
# Quieter, faster startup/logging.
logging_collector = off
EOF
  ok "Cluster initialized and tuned for fast tests."
}

cluster_running() {
  pg_ctl -D "$PG_DATA" status >/dev/null 2>&1
}

start_cluster() {
  if cluster_running; then
    ok "PostgreSQL already running."
  else
    log "Starting PostgreSQL on 127.0.0.1:$PG_PORT ..."
    if ! pg_ctl -D "$PG_DATA" -l "$PG_LOG" -w -t 30 start >>"$PG_LOG" 2>&1; then
      err "pg_ctl start failed — tail of $PG_LOG:"; tail -25 "$PG_LOG" >&2 || true
      exit 6
    fi
    ok "PostgreSQL started."
  fi

  # Wait until it actually accepts connections.
  local i
  for i in $(seq 1 30); do
    if pg_ping; then break; fi
    sleep 0.5
  done
  if ! pg_ping; then
    err "Server up but not accepting connections — tail of $PG_LOG:"; tail -25 "$PG_LOG" >&2 || true
    exit 6
  fi

  # Publish the connection string for the test runner to source.
  {
    echo "export ANIMORA_PG_TEST_CONNECTION='$PG_CONN'"
    echo "export ANIMORA_PG_PORT='$PG_PORT'"
  } >"$PG_CONN_FILE"
  ok "Ready. ANIMORA_PG_TEST_CONNECTION written to $PG_CONN_FILE"
  echo "$PG_CONN"
}

stop_cluster() {
  find_pg_bin
  if [ -f "$PG_DATA/PG_VERSION" ] && cluster_running; then
    log "Stopping PostgreSQL ..."
    pg_ctl -D "$PG_DATA" -m fast -w -t 30 stop >>"$PG_LOG" 2>&1 || true
    ok "Stopped."
  else
    ok "PostgreSQL not running."
  fi
}

case "${1:-install}" in
  install|"")
    install_packages
    init_cluster
    start_cluster
    ;;
  status)
    find_pg_bin
    if [ -f "$PG_DATA/PG_VERSION" ] && cluster_running && pg_ping; then
      ok "PostgreSQL is UP on 127.0.0.1:$PG_PORT"
      echo "ANIMORA_PG_TEST_CONNECTION=$PG_CONN"
    else
      warn "PostgreSQL is DOWN (run: scripts/sandbox/install-postgres.sh)"
      exit 1
    fi
    ;;
  stop)    stop_cluster ;;
  restart) stop_cluster; find_pg_bin; start_cluster ;;
  nuke)
    stop_cluster
    log "Deleting data dir $PG_DATA ..."
    rm -rf "$PG_DATA" "$PG_CONN_FILE"
    ok "Nuked. Next 'install' will re-initdb."
    ;;
  *)
    err "Unknown command: $1 (use: install|status|stop|restart|nuke)"
    exit 2
    ;;
esac
