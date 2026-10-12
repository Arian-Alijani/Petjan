#!/usr/bin/env bash
# ──────────────────────────────────────────────────────────────────────────────
#  Animora — اسکریپت ساخت اشتراک تستی ۱۰۰ روزه
#  این اسکریپت را روی همان سرور دبیانی که Animora.Server و PostgreSQL روی آن است اجرا کن.
#
#  کاری که می‌کند (کاملاً هماهنگ با مدل سرور):
#    1. یک Plan فعال (کد: standard) می‌سازد اگر وجود نداشته باشد.
#    2. برای Tenant موردنظر یک Subscription فعال ۱۰۰ روزه می‌سازد
#       (یا اگر از قبل اشتراک دارد، همان را ۱۰۰ روز فعال/تمدید می‌کند).
#    3. LicenseGrant فعال را دقیقاً مثل LicenseGrantProjector از روی Subscription می‌سازد.
#    4. اختیاری: اگر یوزر/رمز بدهی، یک کاربر Manager فعال هم برای همان Tenant می‌سازد.
#
#  پس از اجرا: وارد برنامه دسکتاپ شو → برنامه خودش device را register و
#  مجوز (/licensing/activate) را می‌گیرد و قابلیت‌ها فعال می‌شوند.
# ──────────────────────────────────────────────────────────────────────────────
set -euo pipefail

# ========== ۱) تنظیمات — این‌ها را خودت عوض کن ==========
# کد مجموعه‌ای که موقع ساخت اکانت در برنامه وارد کردی (همان TenantCode در صفحه ورود):
TENANT_CODE="${TENANT_CODE:-خودت-عوضش-کن}"

# تعداد روزهای اشتراک:
DAYS="${DAYS:-100}"

# (اختیاری) اگر می‌خواهی اسکریپت یک کاربرِ جدید هم بسازد، این‌ها را پر کن.
# اگر خالی بگذاری، فقط اشتراک برای Tenant موجود ساخته می‌شود و کاربر فعلی‌ات کار می‌کند.
NEW_USER="${NEW_USER:-}"            # مثلا: testadmin
NEW_PASSWORD="${NEW_PASSWORD:-}"    # مثلا: Test-Pass-1234
NEW_USER_DISPLAY="${NEW_USER_DISPLAY:-Test Admin}"

# رشته اتصال دیتابیس. اگر سرویس systemd داری، همان Database__ConnectionString است.
# نمونه: "Host=localhost;Port=5432;Database=animora;Username=animora_app;Password=رمز"
# اسکریپت تلاش می‌کند خودش از متغیر محیطی Database__ConnectionString بخواند.
DB_CONN="${DB_CONN:-${Database__ConnectionString:-}}"

# ========== از این‌جا به بعد را دست نزن ==========

RED=$'\e[31m'; GRN=$'\e[32m'; YLW=$'\e[33m'; NC=$'\e[0m'
die() { echo "${RED}✗ $*${NC}" >&2; exit 1; }
ok()  { echo "${GRN}✓ $*${NC}"; }
info(){ echo "${YLW}• $*${NC}"; }

command -v psql >/dev/null 2>&1 || die "psql نصب نیست. اجرا کن: apt install -y postgresql-client"

[ "$TENANT_CODE" != "خودت-عوضش-کن" ] || die "اول TENANT_CODE را در بالای فایل (یا با متغیر محیطی) تنظیم کن."

# ---- تبدیل connection-string به آرگومان‌های psql ----
# اگر DB_CONN دادی (قالب .NET)، آن را به libpq تبدیل می‌کنیم؛ وگرنه فرض بر PG* envها.
PSQL_ARGS=()
if [ -n "$DB_CONN" ]; then
  # Host=...;Port=...;Database=...;Username=...;Password=...  → PG* envها
  _get() { echo "$DB_CONN" | tr ';' '\n' | grep -iE "^\s*$1\s*=" | head -n1 | cut -d= -f2- | xargs; }
  PGHOST="$(_get 'Host')";     PGPORT="$(_get 'Port')"
  PGDATABASE="$(_get 'Database')"; PGUSER="$(_get 'Username')"; PGPASSWORD="$(_get 'Password')"
  [ -n "${PGPORT}" ] || PGPORT=5432
  export PGHOST PGPORT PGDATABASE PGUSER PGPASSWORD
  info "اتصال: host=$PGHOST port=$PGPORT db=$PGDATABASE user=$PGUSER"
else
  info "DB_CONN داده نشد؛ از متغیرهای PGHOST/PGDATABASE/… محیط استفاده می‌شود."
fi

PSQL() { psql -v ON_ERROR_STOP=1 -qtA "$@"; }

# تست اتصال
PSQL -c "SELECT 1" >/dev/null 2>&1 || die "اتصال به دیتابیس برقرار نشد. DB_CONN یا PG* را بررسی کن."
ok "اتصال به PostgreSQL برقرار شد."

# اگر قرار است کاربر بسازیم، رمزش را با همان الگوریتم ASP.NET Core Identity هش می‌کنیم.
# PasswordHasher v3 => prefix 0x01، HMAC-SHA256، 100000 تکرار، نمک ۱۶ بایت، ساب‌کی ۳۲ بایت.
PWHASH=""
if [ -n "$NEW_USER" ] && [ -n "$NEW_PASSWORD" ]; then
  command -v python3 >/dev/null 2>&1 || die "برای ساخت کاربر به python3 نیاز است (apt install -y python3)."
  PWHASH="$(NEW_PASSWORD="$NEW_PASSWORD" python3 - <<'PY'
import os, base64, hashlib, struct
pw = os.environ["NEW_PASSWORD"].encode("utf-8")
salt = os.urandom(16)
iters = 100_000
subkey = hashlib.pbkdf2_hmac("sha256", pw, salt, iters, dklen=32)
# Identity v3 format: 0x01 | prf(=1,SHA256) | iter | saltLen | salt | subkey  (big-endian uint32)
blob = b"\x01" + struct.pack(">III", 1, iters, len(salt)) + salt + subkey
print(base64.b64encode(blob).decode())
PY
)"
  [ -n "$PWHASH" ] || die "ساخت هش رمز ناموفق بود."
  ok "هش رمز برای کاربر '$NEW_USER' ساخته شد (سازگار با ASP.NET Core Identity v3)."
fi

info "در حال اعمال تغییرات (در یک تراکنش)…"

# همه کار داخل یک تراکنش psql با توابع کمکی PL/pgSQL (ULID سازگار با IdGenerator).
# stderr را به stdout هدایت می‌کنیم تا پیام‌های NOTICE هم نمایش داده شوند.
set +e
RESULT="$(NEW_USER="$NEW_USER" NEW_USER_DISPLAY="$NEW_USER_DISPLAY" PWHASH="$PWHASH" \
  TENANT_CODE="$TENANT_CODE" DAYS="$DAYS" PSQL 2>&1 <<'SQL'
\set tenant_code `echo "$TENANT_CODE"`
\set days        `echo "$DAYS"`
\set new_user    `echo "$NEW_USER"`
\set new_disp    `echo "$NEW_USER_DISPLAY"`
\set pwhash      `echo "$PWHASH"`

BEGIN;

-- ULID سازگار با Animora.Domain.Primitives.IdGenerator:
-- ۱۰ کاراکتر زمان (۴۸ بیت میلی‌ثانیه) + ۱۶ کاراکتر رندوم (۸۰ بیت)، الفبای Crockford.
-- بدون وابستگی به pgcrypto: تایم‌استمپ ۴۸ بیتی + ۸۰ بیت رندوم با random()،
-- در قالب ۲۶ کاراکتر Crockford base32 (دقیقاً مثل Animora IdGenerator).
CREATE OR REPLACE FUNCTION pg_temp.animora_ulid() RETURNS text LANGUAGE plpgsql AS $f$
DECLARE
  alphabet constant text := '0123456789ABCDEFGHJKMNPQRSTVWXYZ';
  ms bigint := (extract(epoch from clock_timestamp())*1000)::bigint;
  res text := '';
  i int; v bigint; grp bigint; part text; j int;
BEGIN
  -- ۱۰ کاراکتر تایم‌استمپ (big-endian)
  v := ms;
  FOR i IN 1..10 LOOP
    res := substr(alphabet, (v & 31)::int + 1, 1) || res;
    v := v >> 5;
  END LOOP;
  -- دو گروه ۴۰ بیتی رندوم، هرکدام ۸ کاراکتر
  FOR i IN 0..1 LOOP
    grp := (floor(random()*1099511627776))::bigint;  -- 2^40
    part := '';
    FOR j IN 1..8 LOOP
      part := substr(alphabet, (grp & 31)::int + 1, 1) || part;
      grp := grp >> 5;
    END LOOP;
    res := res || part;
  END LOOP;
  RETURN res;
END;
$f$;

DO $do$
DECLARE
  v_tenant_code text := trim(:'tenant_code');
  v_days        int  := (:'days')::int;
  v_new_user    text := nullif(trim(:'new_user'), '');
  v_new_disp    text := :'new_disp';
  v_pwhash      text := nullif(:'pwhash', '');
  v_now         timestamptz := now();
  v_until       timestamptz;
  v_tenant_id   text;
  v_plan_id     text;
  v_sub_id      text;
  v_seat        int := 5;
  v_devices_per_seat int := 2;   -- پیش‌فرض LicensingOptions.DevicesPerSeat
  v_max_dev     int;
  v_user_id     text;
BEGIN
  v_until := v_now + (v_days || ' days')::interval;

  -- Tenant
  SELECT "Id" INTO v_tenant_id FROM public."Tenants" WHERE "Code" = v_tenant_code;
  IF v_tenant_id IS NULL THEN
    RAISE EXCEPTION 'Tenant با کد % پیدا نشد. اول در برنامه اکانت بساز یا TENANT_CODE را درست بده.', v_tenant_code;
  END IF;
  RAISE NOTICE 'TenantId = %', v_tenant_id;

  -- مطمئن شو Tenant فعال است (Status: 0=Active طبق TenantStatus)
  UPDATE public."Tenants" SET "Status" = 0 WHERE "Id" = v_tenant_id AND "Status" <> 0;

  -- Plan فعال (standard) — معمولاً هنگام migration با Id ثابت seed شده است.
  SELECT "Id", "DefaultSeatLimit" INTO v_plan_id, v_seat
    FROM public."Plans" WHERE "Code" = 'standard' AND "IsActive" = true LIMIT 1;
  IF v_plan_id IS NULL THEN
    v_plan_id := pg_temp.animora_ulid();
    INSERT INTO public."Plans"
      ("Id","Code","Name","DefaultSeatLimit","MonthlyAmountMinor","Currency","TrialDays","IsActive","CreatedUtc")
      VALUES (v_plan_id,'standard','Standard',5,0,'IRT',0,true,v_now);
    v_seat := 5;
    RAISE NOTICE 'Plan «standard» ساخته شد: %', v_plan_id;
  ELSE
    RAISE NOTICE 'Plan «standard» موجود بود: % (seats=%)', v_plan_id, v_seat;
  END IF;

  v_max_dev := v_seat * v_devices_per_seat;

  -- Subscription (یکتا به ازای Tenant). اگر هست فعال/۱۰۰روزه کن؛ اگر نیست بساز.
  SELECT "Id" INTO v_sub_id FROM public."Subscriptions" WHERE "TenantId" = v_tenant_id;
  IF v_sub_id IS NULL THEN
    v_sub_id := pg_temp.animora_ulid();
    INSERT INTO public."Subscriptions"
      ("Id","TenantId","PlanId","Status","SeatLimit",
       "CurrentPeriodStartUtc","CurrentPeriodEndUtc","GraceEndsUtc","CancelRequestedUtc","CancelAtPeriodEnd",
       "NextPeriodSequence","NextEventSequence","ConcurrencyStamp","CreatedUtc","UpdatedUtc")
      VALUES (v_sub_id, v_tenant_id, v_plan_id, 1, v_seat,
              v_now, v_until, NULL, NULL, false,
              1, 1, gen_random_uuid(), v_now, v_now);
    RAISE NOTICE 'Subscription ساخته شد: % (فعال تا %)', v_sub_id, v_until;
  ELSE
    UPDATE public."Subscriptions"
       SET "Status" = 1,                       -- Active
           "PlanId" = v_plan_id,
           "SeatLimit" = v_seat,
           "CurrentPeriodStartUtc" = v_now,
           "CurrentPeriodEndUtc"   = v_until,
           "GraceEndsUtc" = NULL,
           "CancelRequestedUtc" = NULL,
           "CancelAtPeriodEnd" = false,
           "NextEventSequence" = "NextEventSequence" + 1,
           "ConcurrencyStamp" = gen_random_uuid(),
           "UpdatedUtc" = v_now
     WHERE "Id" = v_sub_id;
    RAISE NOTICE 'Subscription موجود به‌روز شد: % (فعال تا %)', v_sub_id, v_until;
  END IF;

  -- LicenseGrant  (همان کاری که LicenseGrantProjector می‌کند: Status 0=Active)
  INSERT INTO public."LicenseGrants"
    ("TenantId","SubscriptionId","PlanCode","SeatLimit","MaxDevices",
     "ValidUntilUtc","Status","Sequence","SourceEventSequence","UpdatedUtc")
    VALUES (v_tenant_id, v_sub_id, 'standard', v_seat, v_max_dev,
            v_until, 0, 1,
            (SELECT "NextEventSequence" - 1 FROM public."Subscriptions" WHERE "Id" = v_sub_id),
            v_now)
  ON CONFLICT ("TenantId") DO UPDATE SET
     "SubscriptionId" = EXCLUDED."SubscriptionId",
     "PlanCode"       = EXCLUDED."PlanCode",
     "SeatLimit"      = EXCLUDED."SeatLimit",
     "MaxDevices"     = EXCLUDED."MaxDevices",
     "ValidUntilUtc"  = EXCLUDED."ValidUntilUtc",
     "Status"         = 0,
     "Sequence"       = public."LicenseGrants"."Sequence" + 1,
     "SourceEventSequence" = EXCLUDED."SourceEventSequence",
     "UpdatedUtc"     = EXCLUDED."UpdatedUtc";
  RAISE NOTICE 'LicenseGrant فعال شد تا %', v_until;

  -- (اختیاری) ساخت کاربر Manager فعال
  IF v_new_user IS NOT NULL AND v_pwhash IS NOT NULL THEN
    IF EXISTS (SELECT 1 FROM public."Users" WHERE "TenantId" = v_tenant_id AND "UserName" = lower(v_new_user)) THEN
      RAISE NOTICE 'کاربر % از قبل وجود دارد؛ فقط رمزش به‌روز و فعال شد.', v_new_user;
      UPDATE public."Users"
         SET "PasswordHash" = v_pwhash, "Status" = 0, "MustChangePassword" = false,
             "FailedAttemptCount" = 0, "LockoutEndUtc" = NULL
       WHERE "TenantId" = v_tenant_id AND "UserName" = lower(v_new_user)
       RETURNING "Id" INTO v_user_id;
    ELSE
      v_user_id := pg_temp.animora_ulid();
      -- ستون‌ها دقیقاً مطابق جدول Users (migration 102_Identity):
      -- Version/Deleted را نمی‌دهیم؛ trigger و default خودشان مقدار می‌دهند.
      INSERT INTO public."Users"
        ("Id","UserName","DisplayName","PasswordHash","SecurityStamp","Status",
         "LastLoginUtc","FailedAttemptCount","LockoutEndUtc","MustChangePassword","FirstFailureUtc",
         "TenantId","UpdatedAt")
        VALUES (v_user_id, lower(v_new_user), v_new_disp, v_pwhash,
                substr(pg_temp.animora_ulid(),1,26), 0,
                NULL, 0, NULL, false, NULL, v_tenant_id, v_now);
      RAISE NOTICE 'کاربر % ساخته شد: %', v_new_user, v_user_id;
    END IF;

    -- عضویت Manager فعال (Role: 0=Manager, Status: 0=Active طبق StaffRole/MembershipStatus).
    -- ستون‌ها مطابق جدول StaffMemberships؛ Version/Deleted به‌عهده trigger/default.
    IF NOT EXISTS (SELECT 1 FROM public."StaffMemberships"
                    WHERE "TenantId" = v_tenant_id AND "UserId" = v_user_id AND "EndedUtc" IS NULL) THEN
      INSERT INTO public."StaffMemberships"
        ("Id","UserId","Role","Status","JoinedUtc","EndedUtc",
         "WorkStartMinute","WorkEndMinute","WorkDaysMask","DeniedPermissionCodes","ExtraPermissionCodes",
         "TenantId","UpdatedAt")
        VALUES (pg_temp.animora_ulid(), v_user_id, 0, 0, v_now, NULL,
                0, 1440, 127, '{}', '{}', v_tenant_id, v_now);
      RAISE NOTICE 'عضویت Manager فعال برای کاربر ساخته شد.';
    END IF;
  END IF;

  RAISE NOTICE '──────── انجام شد ────────';
END
$do$;

COMMIT;
SQL
)"
PSQL_EXIT=$?
set -e

echo "$RESULT" | sed 's/^NOTICE:  /  → /'
if [ "$PSQL_EXIT" -ne 0 ]; then
  die "اجرای SQL با خطا متوقف شد (تراکنش rollback شد). پیام خطا را در بالا ببین."
fi
ok "تمام شد. حالا در برنامه دسکتاپ خارج شو و دوباره وارد شو تا مجوز فعال شود."
echo
echo "${YLW}نکته:${NC} برنامه هنگام ورود، دستگاه را ثبت و مجوز را از مسیر /licensing/activate می‌گیرد."
echo "اگر قابلیت‌ها بلافاصله فعال نشد، یک‌بار برنامه را ببند و دوباره باز کن."
