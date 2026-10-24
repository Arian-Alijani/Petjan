#!/usr/bin/env bash
# One-shot diagnostic: what actually limits the money path at 50 concurrent desktops?
cd /opt/animora || exit 1
export ANIMORA_LT_SERVER_URL=http://127.0.0.1:5000 ANIMORA_LT_TENANT=animora-loadtest
export ANIMORA_LT_USER=manager ANIMORA_LT_PASSWORD='Test-Pass-1234' ANIMORA_LT_OUTPUT=/tmp/live-test4
export ANIMORA_LT_LEVELS=50 ANIMORA_LT_DURATION=80 ANIMORA_LT_COOLDOWN=1
export ANIMORA_LT_THINK_MIN_MS=2000 ANIMORA_LT_THINK_MAX_MS=9000
export ANIMORA_LT_AMBIENT_POLL_SECONDS=5 ANIMORA_LT_MAX_DEVICES=64 ANIMORA_LT_PROFILES=sales-rush
dotnet tests/Animora.LiveTest/bin/Release/net10.0/animora-livetest.dll capacity > /tmp/live-test4/diag.log 2>&1 &
LOADPID=$!

sleep 42
echo "=== PG settings ==="
sudo -u postgres psql -d animora -Atc "show synchronous_commit" -c "show wal_sync_method" -c "show max_connections" -c "show shared_buffers"

C0=$(sudo -u postgres psql -d animora -Atc "select xact_commit from pg_stat_database where datname='animora'")
T0=$(date +%s)

for i in $(seq 1 8); do
  echo "--- sample $i ---"
  echo "  ACTIVE:"
  sudo -u postgres psql -d animora -Atc "select coalesce(wait_event_type,'CPU')||'/'||coalesce(wait_event,'-')||' | '||left(regexp_replace(query,'\s+',' ','g'),70) from pg_stat_activity where datname='animora' and pid<>pg_backend_pid() and state='active' limit 4;" | sed 's/^/    /'
  echo "  UNGRANTED LOCKS:"
  sudo -u postgres psql -d animora -Atc "select coalesce(c.relname,'(none)')||' x'||count(*) from pg_locks l left join pg_class c on c.oid=l.relation where not l.granted group by c.relname;" | sed 's/^/    /'
  echo "  TOP CPU:"
  ps -eo pcpu,comm --sort=-pcpu | head -5 | tail -4 | sed 's/^/    /'
  sleep 3
done

C1=$(sudo -u postgres psql -d animora -Atc "select xact_commit from pg_stat_database where datname='animora'")
T1=$(date +%s)
echo "=== commits in $((T1-T0))s: $((C1-C0))  => $(( (C1-C0) / (T1-T0) )) commits/s ==="
wait $LOADPID
grep -E "users:" /tmp/live-test4/diag.log | tail -2
