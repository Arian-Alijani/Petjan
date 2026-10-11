#!/usr/bin/env bash
# =============================================================================
# pg-init-roles.sh — provision the two-role model for the PG gate lanes (G07).
#
#   animora_owner  LOGIN CREATEDB CREATEROLE  — runs migrations (DDL) and the test fixtures
#                                               (CREATE DATABASE / disposable roles). NOT superuser.
#   animora_app    LOGIN NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE
#                  — the runtime shape: table DML + function EXECUTE only, NO DDL, NO ownership.
#
# Then (optionally, --migrate) creates database "animora_gate", applies BOTH server migrations
# as OWNER, grants the app role DML/EXECUTE, and PROVES the app role cannot do DDL and has no
# BYPASSRLS. Idempotent.
#
# Env (set by the workflow): PGHOST PGPORT PG_BOOTSTRAP_USER PG_BOOTSTRAP_PASSWORD
#                            PG_OWNER_PASSWORD PG_APP_PASSWORD
# =============================================================================
set -euo pipefail

: "${PGHOST:=127.0.0.1}" "${PGPORT:=5432}"
: "${PG_BOOTSTRAP_USER:=postgres}" "${PG_BOOTSTRAP_PASSWORD:?PG_BOOTSTRAP_PASSWORD required}"
: "${PG_OWNER_PASSWORD:?PG_OWNER_PASSWORD required}" "${PG_APP_PASSWORD:?PG_APP_PASSWORD required}"
export PGHOST PGPORT

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
DO_MIGRATE=0; [ "${1:-}" = "--migrate" ] && DO_MIGRATE=1

boot() { PGUSER="$PG_BOOTSTRAP_USER" PGPASSWORD="$PG_BOOTSTRAP_PASSWORD" psql -v ON_ERROR_STOP=1 -qtAX "$@"; }
asrole() { local u="$1" p="$2"; shift 2; PGUSER="$u" PGPASSWORD="$p" psql -v ON_ERROR_STOP=1 -qtAX "$@"; }

echo "── pg-init-roles: creating roles"
boot -d postgres <<SQL
DO \$\$ BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname='animora_owner') THEN
    CREATE ROLE animora_owner LOGIN PASSWORD '${PG_OWNER_PASSWORD}' NOSUPERUSER CREATEDB CREATEROLE;
  END IF;
  IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname='animora_app') THEN
    CREATE ROLE animora_app LOGIN PASSWORD '${PG_APP_PASSWORD}' NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE;
  END IF;
END \$\$;
SQL

[ "$DO_MIGRATE" = "1" ] || { echo "✓ roles ready"; exit 0; }

DB=animora_gate
echo "── pg-init-roles: creating $DB owned by animora_owner and migrating AS OWNER"
boot -d postgres -c "DROP DATABASE IF EXISTS $DB WITH (FORCE)"
boot -d postgres -c "CREATE DATABASE $DB OWNER animora_owner"
OWNER_CONN="Host=$PGHOST;Port=$PGPORT;Username=animora_owner;Password=$PG_OWNER_PASSWORD;Database=$DB"

EF="$REPO_ROOT/.sandbox/tools/dotnet-ef"; [ -x "$EF" ] || EF="dotnet ef"
for ctx in AnimoraServerDbContext AnimoraKeyRingDbContext; do
  ( cd "$REPO_ROOT" && ANIMORA_PG_DESIGN_CONNECTION="$OWNER_CONN" $EF database update --context "$ctx" \
      --project src/Animora.Persistence.PostgreSql --startup-project src/Animora.Persistence.PostgreSql )
done

echo "── pg-init-roles: granting DML + EXECUTE (no DDL, no BYPASSRLS) to animora_app"
asrole animora_owner "$PG_OWNER_PASSWORD" -d "$DB" <<'SQL'
REVOKE ALL ON SCHEMA public FROM PUBLIC;
GRANT USAGE ON SCHEMA public TO animora_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO animora_app;
GRANT USAGE, SELECT, UPDATE ON ALL SEQUENCES IN SCHEMA public TO animora_app;
GRANT EXECUTE ON ALL FUNCTIONS IN SCHEMA public TO animora_app;
SQL

echo "── pg-init-roles: proving the app role is least-privilege"
flags="$(boot -d postgres -c "SELECT rolsuper::int || rolbypassrls::int || rolcreatedb::int || rolcreaterole::int FROM pg_roles WHERE rolname='animora_app'")"
[ "$flags" = "0000" ] || { echo "✗ animora_app has elevated attributes ($flags)"; exit 1; }
if asrole animora_app "$PG_APP_PASSWORD" -d "$DB" -c 'CREATE TABLE public.__app_ddl_probe(i int)' >/dev/null 2>&1; then
  echo "✗ animora_app could run DDL"; exit 1
fi
asrole animora_app "$PG_APP_PASSWORD" -d "$DB" -c 'SELECT count(*) FROM public."Tenants"' >/dev/null \
  || { echo "✗ animora_app cannot read migrated tables"; exit 1; }
echo "✓ roles + migrations ready: owner migrates, app role is DML-only / NOBYPASSRLS / no DDL"
