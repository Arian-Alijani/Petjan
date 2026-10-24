#!/usr/bin/env bash
# Ramps sales-rush from 50 to 150 while sampling lock waits, to find where a code-level
# serialization point (if any) reappears.
cd /opt/animora || exit 1
OUT=/tmp/live-test9
mkdir -p $OUT

export ANIMORA_LT_SERVER_URL=http://127.0.0.1:5000
export ANIMORA_LT_TENANT=animora-loadtest
export ANIMORA_LT_USER=manager
export ANIMORA_LT_PASSWORD='Test-Pass-1234'
export ANIMORA_LT_OUTPUT=$OUT
export ANIMORA_LT_PROFILES=sales-rush
export ANIMORA_LT_AUTO_RAMP=1
export ANIMORA_LT_START_LEVEL=60
export ANIMORA_LT_STEP=10
export ANIMORA_LT_MAX_LEVEL=150
export ANIMORA_LT_BREAK_CONFIRM=99
export ANIMORA_LT_DURATION=25
export ANIMORA_LT_COOLDOWN=2
export ANIMORA_LT_THINK_MIN_MS=2000
export ANIMORA_LT_THINK_MAX_MS=9000
export ANIMORA_LT_AMBIENT_POLL_SECONDS=5
export ANIMORA_LT_MAX_DEVICES=64
export ANIMORA_LT_P95_LIMIT_MS=10000
export ANIMORA_LT_SSH=1
export ANIMORA_LT_SSH_HOST=127.0.0.1
export ANIMORA_LT_SSH_USER=root
export ANIMORA_LT_SSH_KEY=/root/.ssh/animora_monitor

dotnet tests/Animora.LiveTest/bin/Release/net10.0/animora-livetest.dll capacity > $OUT/run.log 2>&1 &
LOAD=$!

# Sample until the ramp ends.
while kill -0 $LOAD 2>/dev/null; do
  LEVEL=$(grep -oE '^ +[0-9]+ users:' $OUT/run.log 2>/dev/null | tail -1 | grep -oE '[0-9]+' | head -1)
  LOCKS=$(sudo -u postgres psql -d animora -Atc \
    "select coalesce(string_agg(coalesce(c.relname,'?')||'x'||n, ','),'-') from (select relation, count(*) n from pg_locks where not granted group by relation) l left join pg_class c on c.oid=l.relation;" 2>/dev/null)
  MAXAGE=$(sudo -u postgres psql -d animora -Atc \
    "select coalesce(max(round(extract(epoch from now()-query_start)*1000))::text,'0') from pg_stat_activity where datname='animora' and state='active' and pid<>pg_backend_pid();" 2>/dev/null)
  echo "$(date +%H:%M:%S) level=${LEVEL:-?} locks=${LOCKS:-?} maxActiveMs=${MAXAGE:-?}"
  sleep 2
done

wait $LOAD
echo "=== result ==="
grep -E "users:" $OUT/run.log
