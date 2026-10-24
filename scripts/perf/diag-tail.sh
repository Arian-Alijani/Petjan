#!/usr/bin/env bash
# Samples what the DB is actually waiting on during a 45-desktop sales-rush level.
cd /opt/animora || exit 1
export ANIMORA_LT_SERVER_URL=http://127.0.0.1:5000 ANIMORA_LT_TENANT=animora-loadtest
export ANIMORA_LT_USER=manager ANIMORA_LT_PASSWORD='Test-Pass-1234' ANIMORA_LT_OUTPUT=/tmp/live-test7
export ANIMORA_LT_LEVELS=45 ANIMORA_LT_DURATION=70 ANIMORA_LT_COOLDOWN=1
export ANIMORA_LT_THINK_MIN_MS=2000 ANIMORA_LT_THINK_MAX_MS=9000
export ANIMORA_LT_AMBIENT_POLL_SECONDS=5 ANIMORA_LT_MAX_DEVICES=64 ANIMORA_LT_PROFILES=sales-rush

dotnet tests/Animora.LiveTest/bin/Release/net10.0/animora-livetest.dll capacity > /tmp/live-test7/sample.log 2>&1 &
LOAD=$!
sleep 38

for i in $(seq 1 22); do
  TS=$(date +%H:%M:%S)
  LOCKS=$(sudo -u postgres psql -d animora -Atc \
    "select coalesce(string_agg(coalesce(c.relname,'?')||'x'||n, ','),'none') from (select relation, count(*) n from pg_locks where not granted group by relation) l left join pg_class c on c.oid=l.relation;" 2>/dev/null)
  LONG=$(sudo -u postgres psql -d animora -Atc \
    "select coalesce(wait_event_type,'CPU')||'/'||coalesce(wait_event,'-')||' '||round(extract(epoch from now()-query_start)*1000)||'ms ['||left(regexp_replace(query,'\s+',' ','g'),40)||']' from pg_stat_activity where datname='animora' and state='active' and now()-query_start > interval '250 ms' and pid<>pg_backend_pid() limit 2;" 2>/dev/null)
  echo "$TS locks=${LOCKS:-?} | slow=${LONG:-none}"
  sleep 1
done

wait $LOAD
grep -E "users:" /tmp/live-test7/sample.log | tail -1
