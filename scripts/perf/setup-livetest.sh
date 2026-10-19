#!/usr/bin/env bash
# ──────────────────────────────────────────────────────────────────────────────
#  Animora — آماده‌سازی کامل و «تمیز» محیط LiveTest روی سرور دبیان
#
#  این را روی همان سرور دبیانی اجرا کن که Animora.Server و PostgreSQL روی آن است.
#  یک دستور، همه‌چیز را آماده می‌کند — بدون هیچ کار دستی دیگر:
#
#    1.  اگر Tenant تستی از قبل باشد، کلّ داده‌اش را «کاملاً و تمیز» پاک می‌کند
#        (همهٔ جدول‌هایی که ستون TenantId دارند + ردیف خود Tenant).
#        هیچ دستگاه/سشن/توکن قدیمی باقی نمی‌ماند ⇒ دیگر مشکل «دسترسی/دیوایس» نداری.
#    2.  Tenant تستی را از صفر می‌سازد (نیازی نیست اول در برنامه اکانت بسازی).
#    3.  Plan فعال «standard» را می‌سازد اگر نباشد.
#    4.  Subscription فعال ۱۰۰ روزه + LicenseGrant فعال (دقیقاً مثل LicenseGrantProjector).
#    5.  کاربر Manager فعال (پیش‌فرض: manager) — همان حسابی که LiveTest با آن وارد می‌شود.
#    6.  کاربر Doctor فعال — پیش‌شرطِ سناریوهای نوبت‌دهی/ویزیت (بدونش SKIP می‌شوند).
#
#  خروجی: دقیقاً همان مقادیری که باید در ویندوز به run-livetest.ps1 بدهی را چاپ می‌کند.
#  بقیهٔ دادهٔ موردنیاز تست (مالک/حیوان/محصول/…) را خودِ LiveTest می‌سازد.
# ──────────────────────────────────────────────────────────────────────────────
set -euo pipefail

# ========== تنظیمات (همه اختیاری — پیش‌فرض‌ها دقیقاً با LiveTest هماهنگ‌اند) ==========
# کد مجموعه (TenantCode صفحهٔ ورود). پیش‌فرض LiveTest همین است:
TENANT_CODE="${TENANT_CODE:-ANIMORA-LOADTEST}"
TENANT_DISPLAY="${TENANT_DISPLAY:-Animora LoadTest}"

# حساب مدیری که LiveTest با آن وارد می‌شود (ANIMORA_LT_USER / ANIMORA_LT_PASSWORD):
NEW_USER="${NEW_USER:-manager}"
NEW_PASSWORD="${NEW_PASSWORD:-Test-Pass-1234}"
NEW_USER_DISPLAY="${NEW_USER_DISPLAY:-Test Manager}"

# دکتر فعال (برای سناریوهای ویزیت/نوبت‌دهی). این را تست خودش نمی‌تواند بسازد:
DOCTOR_USER="${DOCTOR_USER:-doctor}"
DOCTOR_PASSWORD="${DOCTOR_PASSWORD:-Test-Pass-1234}"
DOCTOR_DISPLAY="${DOCTOR_DISPLAY:-Test Doctor}"

# طول اشتراک:
DAYS="${DAYS:-100}"

# منطقهٔ زمانی و واحد پول پیش‌فرض Tenant:
TENANT_TZ="${TENANT_TZ:-Asia/Tehran}"
TENANT_CCY="${TENANT_CCY:-IRT}"

# رشتهٔ اتصال دیتابیس (قالب .NET). اگر سرویس systemd داری همان Database__ConnectionString است.
# نمونه: "Host=localhost;Port=5432;Database=animora;Username=animora_app;Password=رمز"
DB_CONN="${DB_CONN:-${Database__ConnectionString:-}}"

# ========== از این‌جا به بعد را دست نزن ==========
RED=$'\e[31m'; GRN=$'\e[32m'; YLW=$'\e[33m'; CYN=$'\e[36m'; NC=$'\e[0m'
die() { echo "${RED}✗ $*${NC}" >&2; exit 1; }
ok()  { echo "${GRN}✓ $*${NC}"; }
info(){ echo "${YLW}• $*${NC}"; }

command -v psql    >/dev/null 2>&1 || die "psql نصب نیست. اجرا کن: apt install -y postgresql-client"
command -v python3 >/dev/null 2>&1 || die "python3 نصب نیست. اجرا کن: apt install -y python3"
[ -n "$TENANT_CODE" ] || die "TENANT_CODE خالی است."

# ---- تبدیل connection-string .NET به envهای libpq ----
if [ -n "$DB_CONN" ]; then
  _get() { echo "$DB_CONN" | tr ';' '\n' | grep -iE "^\s*$1\s*=" | head -n1 | cut -d= -f2- | xargs; }
  PGHOST="$(_get 'Host')";      PGPORT="$(_get 'Port')"
  PGDATABASE="$(_get 'Database')"; PGUSER="$(_get 'Username')"; PGPASSWORD="$(_get 'Password')"
  [ -n "${PGPORT}" ] || PGPORT=5432
  export PGHOST PGPORT PGDATABASE PGUSER PGPASSWORD
  info "اتصال: host=$PGHOST port=$PGPORT db=$PGDATABASE user=$PGUSER"
else
  info "DB_CONN داده نشد؛ از متغیرهای PGHOST/PGDATABASE/… محیط استفاده می‌شود."
fi

PSQL() { psql -v ON_ERROR_STOP=1 -qtA "$@"; }
PSQL -c "SELECT 1" >/dev/null 2>&1 || die "اتصال به دیتابیس برقرار نشد. DB_CONN یا PG* را بررسی کن."
ok "اتصال به PostgreSQL برقرار شد."

# ---- هش رمزها با الگوریتم ASP.NET Core Identity v3 (مثل اسکریپت grant موجود) ----
hash_pw() {
  NEW_PASSWORD="$1" python3 - <<'PY'
import os, base64, hashlib, struct
pw = os.environ["NEW_PASSWORD"].encode("utf-8")
salt = os.urandom(16); iters = 100_000
subkey = hashlib.pbkdf2_hmac("sha256", pw, salt, iters, dklen=32)
blob = b"\x01" + struct.pack(">III", 1, iters, len(salt)) + salt + subkey
print(base64.b64encode(blob).decode())
PY
}
MGR_HASH="$(hash_pw "$NEW_PASSWORD")";    [ -n "$MGR_HASH" ]    || die "هش رمز مدیر ساخته نشد."
DOC_HASH="$(hash_pw "$DOCTOR_PASSWORD")"; [ -n "$DOC_HASH" ]   || die "هش رمز دکتر ساخته نشد."
ok "هش رمزها ساخته شد (سازگار با ASP.NET Core Identity v3)."

info "در حال پاکسازی و بازسازی کامل محیط تست (در یک تراکنش)…"

set +e
RESULT="$(TENANT_CODE="$TENANT_CODE" TENANT_DISPLAY="$TENANT_DISPLAY" \
  TENANT_TZ="$TENANT_TZ" TENANT_CCY="$TENANT_CCY" DAYS="$DAYS" \
  NEW_USER="$NEW_USER" NEW_USER_DISPLAY="$NEW_USER_DISPLAY" MGR_HASH="$MGR_HASH" \
  DOCTOR_USER="$DOCTOR_USER" DOCTOR_DISPLAY="$DOCTOR_DISPLAY" DOC_HASH="$DOC_HASH" \
  PSQL 2>&1 <<'SQL'
\set tenant_code `echo "$TENANT_CODE"`
\set tenant_disp `echo "$TENANT_DISPLAY"`
\set tenant_tz   `echo "$TENANT_TZ"`
\set tenant_ccy  `echo "$TENANT_CCY"`
\set days        `echo "$DAYS"`
\set mgr_user    `echo "$NEW_USER"`
\set mgr_disp    `echo "$NEW_USER_DISPLAY"`
\set mgr_hash    `echo "$MGR_HASH"`
\set doc_user    `echo "$DOCTOR_USER"`
\set doc_disp    `echo "$DOCTOR_DISPLAY"`
\set doc_hash    `echo "$DOC_HASH"`

BEGIN;

-- psql متغیرهای :'x' را داخل بلوک‌های dollar-quoted (DO $do$ … $do$) جایگزین نمی‌کند،
-- بنابراین مقادیر را این‌جا (خارج از بلوک، جایی که جایگزینی انجام می‌شود) داخل تنظیماتِ
-- session می‌گذاریم و داخل DO با current_setting() امن می‌خوانیم. این کار در برابر
-- SQL injection امن است چون مقدار هرگز به‌صورت متن داخل دستور الحاق نمی‌شود.
-- خروجی این دستورات را دور می‌ریزیم تا مقادیر (به‌ویژه هش رمز) در ترمینال چاپ نشوند.
\o /dev/null
SELECT set_config('animora_lt.tenant_code', :'tenant_code', false);
SELECT set_config('animora_lt.tenant_disp', :'tenant_disp', false);
SELECT set_config('animora_lt.tenant_tz',   :'tenant_tz',   false);
SELECT set_config('animora_lt.tenant_ccy',  :'tenant_ccy',  false);
SELECT set_config('animora_lt.days',        :'days',        false);
SELECT set_config('animora_lt.mgr_user',    :'mgr_user',    false);
SELECT set_config('animora_lt.mgr_disp',    :'mgr_disp',    false);
SELECT set_config('animora_lt.mgr_hash',    :'mgr_hash',    false);
SELECT set_config('animora_lt.doc_user',    :'doc_user',    false);
SELECT set_config('animora_lt.doc_disp',    :'doc_disp',    false);
SELECT set_config('animora_lt.doc_hash',    :'doc_hash',    false);
\o

-- ULID سازگار با Animora.Domain.Primitives.IdGenerator (بدون وابستگی به pgcrypto).
CREATE OR REPLACE FUNCTION pg_temp.animora_ulid() RETURNS text LANGUAGE plpgsql AS $f$
DECLARE
  alphabet constant text := '0123456789ABCDEFGHJKMNPQRSTVWXYZ';
  ms bigint := (extract(epoch from clock_timestamp())*1000)::bigint;
  res text := ''; i int; v bigint; grp bigint; part text; j int;
BEGIN
  v := ms;
  FOR i IN 1..10 LOOP res := substr(alphabet, (v & 31)::int + 1, 1) || res; v := v >> 5; END LOOP;
  FOR i IN 0..1 LOOP
    grp := (floor(random()*1099511627776))::bigint; part := '';
    FOR j IN 1..8 LOOP part := substr(alphabet, (grp & 31)::int + 1, 1) || part; grp := grp >> 5; END LOOP;
    res := res || part;
  END LOOP;
  RETURN res;
END;
$f$;

DO $do$
DECLARE
  v_code     text := trim(current_setting('animora_lt.tenant_code'));
  v_disp     text := current_setting('animora_lt.tenant_disp');
  v_tz       text := current_setting('animora_lt.tenant_tz');
  v_ccy      text := current_setting('animora_lt.tenant_ccy');
  v_days     int  := current_setting('animora_lt.days')::int;
  v_mgr_user text := lower(trim(current_setting('animora_lt.mgr_user')));
  v_mgr_disp text := current_setting('animora_lt.mgr_disp');
  v_mgr_hash text := current_setting('animora_lt.mgr_hash');
  v_doc_user text := lower(trim(current_setting('animora_lt.doc_user')));
  v_doc_disp text := current_setting('animora_lt.doc_disp');
  v_doc_hash text := current_setting('animora_lt.doc_hash');
  v_now      timestamptz := now();
  v_until    timestamptz;
  v_tenant   text;
  v_plan     text;
  v_seat     int := 5;
  v_dev_seat int := 2;       -- پیش‌فرض LicensingOptions.DevicesPerSeat
  v_max_dev  int;
  v_sub      text;
  v_mgr_id   text;
  v_doc_id   text;
  r          record;
BEGIN
  v_until := v_now + (v_days || ' days')::interval;

  -- ───────── ۱) پاکسازی کامل Tenant قبلی (اگر باشد) ─────────
  SELECT "Id" INTO v_tenant FROM public."Tenants" WHERE "Code" = v_code;
  IF v_tenant IS NOT NULL THEN
    RAISE NOTICE 'Tenant قبلی پیدا شد (%). در حال پاکسازی کامل…', v_tenant;
    -- روش: چند دور روی همهٔ جدول‌هایی که ستون TenantId دارند DELETE می‌زنیم و در هر
    -- دور، سطرهایی که به‌خاطر وابستگی FK هنوز قابل‌حذف نیستند را نادیده می‌گیریم؛
    -- دورها را آن‌قدر تکرار می‌کنیم تا دیگر هیچ سطری حذف نشود. این روش بدونِ نیاز
    -- به دسترسی superuser کار می‌کند (برخلاف غیرفعال‌کردن تریگرها).
    DECLARE
      v_pass int := 0;
      v_deleted bigint;
      v_total bigint;
    BEGIN
      LOOP
        v_pass := v_pass + 1;
        v_total := 0;
        FOR r IN
          SELECT c.table_name
          FROM information_schema.columns c
          JOIN information_schema.tables t
            ON t.table_schema = c.table_schema AND t.table_name = c.table_name
          WHERE c.table_schema = 'public' AND c.column_name = 'TenantId'
            AND t.table_type = 'BASE TABLE'
            AND c.table_name <> 'Tenants'   -- ردیف خودِ Tenant را در پایان پاک می‌کنیم
        LOOP
          BEGIN
            EXECUTE format('DELETE FROM public.%I WHERE %I = $1', r.table_name, 'TenantId') USING v_tenant;
            GET DIAGNOSTICS v_deleted = ROW_COUNT;
            v_total := v_total + v_deleted;
          EXCEPTION WHEN foreign_key_violation THEN
            -- این جدول هنوز وابستهٔ جدول دیگری است؛ دورِ بعد دوباره تلاش می‌شود.
            NULL;
          END;
        END LOOP;
        EXIT WHEN v_total = 0 OR v_pass > 20;
      END LOOP;
      IF v_pass > 20 THEN
        RAISE EXCEPTION 'پاکسازی پس از ۲۰ دور کامل نشد (چرخهٔ FK غیرمنتظره). دستی بررسی کن.';
      END IF;
    END;
    -- جدول‌های سراسریِ وابسته به Subscription/License و خودِ Tenant.
    DELETE FROM public."LicenseGrants" WHERE "TenantId" = v_tenant;
    DELETE FROM public."Subscriptions" WHERE "TenantId" = v_tenant;
    DELETE FROM public."Tenants"       WHERE "Id"       = v_tenant;
    RAISE NOTICE 'پاکسازی کامل شد.';
  ELSE
    RAISE NOTICE 'Tenant قبلی وجود نداشت — از صفر ساخته می‌شود.';
  END IF;

  -- ───────── ۲) ساخت Tenant از نو ─────────
  v_tenant := pg_temp.animora_ulid();
  INSERT INTO public."Tenants"
    ("Id","DisplayName","LegalName","Timezone","DefaultCurrency","Status","Code","TenantId","UpdatedAt")
    VALUES (v_tenant, v_disp, NULL, v_tz, v_ccy, 0, v_code, v_tenant, v_now);
  RAISE NOTICE 'Tenant ساخته شد: % (code=%)', v_tenant, v_code;

  -- ───────── ۳) Plan فعال standard ─────────
  SELECT "Id","DefaultSeatLimit" INTO v_plan, v_seat
    FROM public."Plans" WHERE "Code" = 'standard' AND "IsActive" = true LIMIT 1;
  IF v_plan IS NULL THEN
    v_plan := pg_temp.animora_ulid();
    INSERT INTO public."Plans"
      ("Id","Code","Name","DefaultSeatLimit","MonthlyAmountMinor","Currency","TrialDays","IsActive","CreatedUtc")
      VALUES (v_plan,'standard','Standard',5,0,'IRT',0,true,v_now);
    v_seat := 5;
    RAISE NOTICE 'Plan «standard» ساخته شد.';
  END IF;
  v_max_dev := v_seat * v_dev_seat;

  -- ───────── ۴) Subscription فعال + LicenseGrant فعال ─────────
  v_sub := pg_temp.animora_ulid();
  INSERT INTO public."Subscriptions"
    ("Id","TenantId","PlanId","Status","SeatLimit",
     "CurrentPeriodStartUtc","CurrentPeriodEndUtc","GraceEndsUtc","CancelRequestedUtc","CancelAtPeriodEnd",
     "NextPeriodSequence","NextEventSequence","ConcurrencyStamp","CreatedUtc","UpdatedUtc")
    VALUES (v_sub, v_tenant, v_plan, 1, v_seat,
            v_now, v_until, NULL, NULL, false,
            1, 1, gen_random_uuid(), v_now, v_now);

  INSERT INTO public."LicenseGrants"
    ("TenantId","SubscriptionId","PlanCode","SeatLimit","MaxDevices",
     "ValidUntilUtc","Status","Sequence","SourceEventSequence","UpdatedUtc")
    VALUES (v_tenant, v_sub, 'standard', v_seat, v_max_dev,
            v_until, 0, 1, 0, v_now)
  ON CONFLICT ("TenantId") DO UPDATE SET
     "SubscriptionId"      = EXCLUDED."SubscriptionId",
     "PlanCode"            = EXCLUDED."PlanCode",
     "SeatLimit"           = EXCLUDED."SeatLimit",
     "MaxDevices"          = EXCLUDED."MaxDevices",
     "ValidUntilUtc"       = EXCLUDED."ValidUntilUtc",
     "Status"              = 0,
     "Sequence"            = public."LicenseGrants"."Sequence" + 1,
     "SourceEventSequence" = EXCLUDED."SourceEventSequence",
     "UpdatedUtc"          = EXCLUDED."UpdatedUtc";
  RAISE NOTICE 'اشتراک و مجوز فعال شد تا % (seats=%, maxDevices=%).', v_until, v_seat, v_max_dev;

  -- ───────── ۵) کاربر Manager فعال ─────────
  v_mgr_id := pg_temp.animora_ulid();
  INSERT INTO public."Users"
    ("Id","UserName","DisplayName","PasswordHash","SecurityStamp","Status",
     "LastLoginUtc","FailedAttemptCount","LockoutEndUtc","MustChangePassword","FirstFailureUtc",
     "TenantId","UpdatedAt")
    VALUES (v_mgr_id, v_mgr_user, v_mgr_disp, v_mgr_hash,
            substr(pg_temp.animora_ulid(),1,26), 0,
            NULL, 0, NULL, false, NULL, v_tenant, v_now);
  INSERT INTO public."StaffMemberships"
    ("Id","UserId","Role","Status","JoinedUtc","EndedUtc",
     "WorkStartMinute","WorkEndMinute","WorkDaysMask","DeniedPermissionCodes","ExtraPermissionCodes",
     "TenantId","UpdatedAt")
    VALUES (pg_temp.animora_ulid(), v_mgr_id, 0, 0, v_now, NULL,
            0, 1440, 127, '{}', '{}', v_tenant, v_now);
  RAISE NOTICE 'کاربر Manager ساخته شد: % (%).', v_mgr_user, v_mgr_id;

  -- ───────── ۶) کاربر Doctor فعال (پیش‌شرط سناریوهای ویزیت/نوبت) ─────────
  v_doc_id := pg_temp.animora_ulid();
  INSERT INTO public."Users"
    ("Id","UserName","DisplayName","PasswordHash","SecurityStamp","Status",
     "LastLoginUtc","FailedAttemptCount","LockoutEndUtc","MustChangePassword","FirstFailureUtc",
     "TenantId","UpdatedAt")
    VALUES (v_doc_id, v_doc_user, v_doc_disp, v_doc_hash,
            substr(pg_temp.animora_ulid(),1,26), 0,
            NULL, 0, NULL, false, NULL, v_tenant, v_now);
  INSERT INTO public."StaffMemberships"
    ("Id","UserId","Role","Status","JoinedUtc","EndedUtc",
     "WorkStartMinute","WorkEndMinute","WorkDaysMask","DeniedPermissionCodes","ExtraPermissionCodes",
     "TenantId","UpdatedAt")
    VALUES (pg_temp.animora_ulid(), v_doc_id, 1, 0, v_now, NULL,
            0, 1440, 127, '{}', '{}', v_tenant, v_now);
  RAISE NOTICE 'کاربر Doctor فعال ساخته شد: % (%).', v_doc_user, v_doc_id;

  RAISE NOTICE '──────── محیط تست تمیز و آماده است ────────';
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

ok "محیط LiveTest کاملاً تمیز و آماده شد."
echo
echo "${CYN}┌─────────────────────────────────────────────────────────────┐${NC}"
echo "${CYN}│  مقادیری که در ویندوز به run-livetest.ps1 می‌دهی:            │${NC}"
echo "${CYN}└─────────────────────────────────────────────────────────────┘${NC}"
echo "  Tenant   : ${GRN}${TENANT_CODE}${NC}"
echo "  User     : ${GRN}${NEW_USER}${NC}"
echo "  Password : ${GRN}${NEW_PASSWORD}${NC}"
echo "  Doctor   : ${GRN}${DOCTOR_USER}${NC}  (رمز: ${DOCTOR_PASSWORD})"
echo
echo "${YLW}نمونهٔ اجرا روی ویندوز (PowerShell):${NC}"
echo "  .\\run-livetest.ps1 -User ${NEW_USER} -Password '${NEW_PASSWORD}' -Tenant ${TENANT_CODE}"
