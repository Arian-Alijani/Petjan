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
#        کد مجموعه همیشه lowercase ذخیره می‌شود — همان شکلی که TenantDirectory.NormalizeCode
#        در سرور انتظار دارد؛ ذخیرهٔ کد با حروف بزرگ باعث CREDENTIALS_INVALID در ورود می‌شد.
#    3.  پنج تعریف نقش سیستمی را می‌سازد (آینهٔ DefaultRolePermissions). بدون این‌ها
#        PermissionEvaluator هیچ مجوزی نمی‌دهد و هر فرمانِ مجازشده PERMISSION_DENIED می‌گیرد.
#    4.  Plan فعال «standard» را می‌سازد اگر نباشد.
#    5.  Subscription فعال ۱۰۰ روزه + LicenseGrant فعال (دقیقاً مثل LicenseGrantProjector).
#    6.  کاربر Manager فعال (پیش‌فرض: manager) — همان حسابی که LiveTest با آن وارد می‌شود.
#    7.  کاربر Doctor فعال — پیش‌شرطِ سناریوهای نوبت‌دهی/ویزیت (بدونش SKIP می‌شوند).
#    8.  در پایان با یک ورود واقعی (curl) سالم‌بودن مسیر ورود را خودآزمایی می‌کند.
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

# سقف دستگاه‌های مجاز (MaxDevices در LicenseGrant). خالی/0 یعنی پیش‌فرض محصول = SeatLimit × 2.
# برای تست ظرفیت لازم است چون هر اجرا چند دیوایس/لایسنس می‌سازد و سقف ۱۰ خیلی زود پر می‌شود:
#   MAX_DEVICES=64 bash scripts/perf/setup-livetest.sh
MAX_DEVICES="${MAX_DEVICES:-0}"

# منطقهٔ زمانی و واحد پول پیش‌فرض Tenant:
TENANT_TZ="${TENANT_TZ:-Asia/Tehran}"
TENANT_CCY="${TENANT_CCY:-IRT}"

# رشتهٔ اتصال دیتابیس (قالب .NET). اگر سرویس systemd داری همان Database__ConnectionString است.
# نمونه: "Host=localhost;Port=5432;Database=animora;Username=animora_app;Password=رمز"
DB_CONN="${DB_CONN:-${Database__ConnectionString:-}}"

# رشتهٔ اتصالِ اختیاری برای «پاک‌سازی». پاک‌کردن یک Tenant کامل باید ردیف‌های جدول‌های
# append-only (StockMovements / JournalEntries / SecurityAudit / ...) را هم حذف کند و این کار
# فقط با یک اتصالِ superuser و حالت replica ممکن است. اگر این متغیر را بدهی، seed با همان
# اتصال انجام می‌شود؛ وگرنه با DB_CONN معمولی (و پاک‌سازی «بهترین تلاش»).
#   نمونه روی همان سرور:  SEED_DB_CONN='Host=/var/run/postgresql;Username=postgres;Database=animora'
DB_CONN="${SEED_DB_CONN:-$DB_CONN}"

# ========== از این‌جا به بعد را دست نزن ==========
RED=$'\e[31m'; GRN=$'\e[32m'; YLW=$'\e[33m'; CYN=$'\e[36m'; NC=$'\e[0m'
die() { echo "${RED}✗ $*${NC}" >&2; exit 1; }
ok()  { echo "${GRN}✓ $*${NC}"; }
info(){ echo "${YLW}• $*${NC}"; }

command -v psql    >/dev/null 2>&1 || die "psql نصب نیست. اجرا کن: apt install -y postgresql-client"
command -v python3 >/dev/null 2>&1 || die "python3 نصب نیست. اجرا کن: apt install -y python3"
[ -n "$TENANT_CODE" ] || die "TENANT_CODE خالی است."

# ---- نرمال‌سازی کد مجموعه (بحرانی) ----
# سرور هنگام ورود، کد واردشده را با TenantDirectory.NormalizeCode به trim + lowercase تبدیل می‌کند و
# بعد آن را با ستون Tenants."Code" مقایسه می‌کند. TenantProvisioner هم هنگام ساخت مجموعه دقیقاً
# همین شکل lowercase را ذخیره می‌کند. پس اگر این اسکریپت کد را با حروف بزرگ ذخیره کند، هیچ‌وقت با
# مقدار نرمال‌شدهٔ سرور برابر نمی‌شود و ورود همیشه CREDENTIALS_INVALID می‌گیرد (بدون هیچ ردی در
# SecurityAudit، چون شکستِ lookup مجموعه audit نمی‌شود).
TENANT_CODE_RAW="$TENANT_CODE"
TENANT_CODE="$(printf '%s' "$TENANT_CODE" | tr -d '[:space:]' | tr '[:upper:]' '[:lower:]')"
[ -n "$TENANT_CODE" ] || die "TENANT_CODE پس از نرمال‌سازی خالی شد."
printf '%s' "$TENANT_CODE" | grep -Eq '^[a-z0-9_-]{1,32}$' \
  || die "کد مجموعه باید ۱ تا ۳۲ کاراکتر از [a-z0-9_-] باشد (مقدار ورودی: '$TENANT_CODE_RAW')."
if [ "$TENANT_CODE_RAW" != "$TENANT_CODE" ]; then
  info "کد مجموعه نرمال شد: '$TENANT_CODE_RAW' → '$TENANT_CODE' (سرور فقط شکل lowercase را می‌شناسد)."
fi

# ---- تبدیل connection-string .NET به envهای libpq ----
if [ -n "$DB_CONN" ]; then
  # نکته: رشتهٔ اتصالِ peer (مثل Username=postgres روی سوکت) ممکن است Password نداشته باشد؛
  # بدون || true این خط با set -eo pipefail کل اسکریپت را بی‌صدا می‌بندد.
  _get() { echo "$DB_CONN" | tr ';' '\n' | grep -iE "^\s*$1\s*=" | head -n1 | cut -d= -f2- | xargs || true; }
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
\set max_dev     `echo "$MAX_DEVICES"`
\set mgr_user    `echo "$NEW_USER"`
\set mgr_disp    `echo "$NEW_USER_DISPLAY"`
\set mgr_hash    `echo "$MGR_HASH"`
\set doc_user    `echo "$DOCTOR_USER"`
\set doc_disp    `echo "$DOCTOR_DISPLAY"`
\set doc_hash    `echo "$DOC_HASH"`

BEGIN;

-- حالت replica تریگرها (از جمله append-only و FK) را در طول همین تراکنش غیرفعال می‌کند تا
-- پاک‌سازیِ کاملِ یک Tenant تستی ممکن باشد؛ وگرنه جدول‌هایی مثل StockMovements / JournalEntries /
-- SecurityAudit هرگز DELETE نمی‌پذیرند و بازسازی محیط شکست می‌خورد. فقط superuser اجازهٔ این
-- تنظیم را دارد؛ اگر نداشت، بی‌صدا رد می‌شود و پاک‌سازی «بهترین تلاش» انجام می‌شود.
DO $role$
BEGIN
  BEGIN
    EXECUTE 'SET LOCAL session_replication_role = replica';
    RAISE NOTICE 'حالت replica فعال شد (تریگرها در طول پاک‌سازی غیرفعال‌اند).';
  EXCEPTION WHEN insufficient_privilege THEN
    RAISE NOTICE 'اتصال superuser نیست؛ پاک‌سازی با تریگرهای فعال انجام می‌شود (جدول‌های append-only قابل پاک‌سازی نیستند).';
  END;
END
$role$;

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
SELECT set_config('animora_lt.max_dev',     :'max_dev',     false);
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
  v_code     text := lower(trim(current_setting('animora_lt.tenant_code')));
  v_disp     text := current_setting('animora_lt.tenant_disp');
  v_tz       text := current_setting('animora_lt.tenant_tz');
  v_ccy      text := current_setting('animora_lt.tenant_ccy');
  v_days     int  := current_setting('animora_lt.days')::int;
  v_max_ovr  int  := current_setting('animora_lt.max_dev')::int;
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
  -- مقایسه بدون حساسیت به حروف است تا مجموعه‌ای که نسخه‌های قبلی این اسکریپت با کدِ
  -- حروف‌بزرگ ساخته‌اند هم پیدا و پاک شود (وگرنه یک Tenant یتیم با کد بزرگ باقی می‌ماند).
  SELECT "Id" INTO v_tenant FROM public."Tenants" WHERE lower("Code") = v_code;
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
            -- ردیف خودِ Tenant و دو جدول فید/ژورنال را در پایان و به‌ترتیبِ درست پاک می‌کنیم:
            -- تریگر ژورنال برای ثبت ردیف حذفِ Tenant به ردیف TenantFeedState نیاز دارد، پس
            -- آن ردیف باید تا لحظهٔ DELETE روی Tenants زنده بماند.
            AND c.table_name NOT IN ('Tenants', 'TenantFeedState', 'TenantJournal')
        LOOP
          BEGIN
            EXECUTE format('DELETE FROM public.%I WHERE %I = $1', r.table_name, 'TenantId') USING v_tenant;
            GET DIAGNOSTICS v_deleted = ROW_COUNT;
            v_total := v_total + v_deleted;
          EXCEPTION WHEN foreign_key_violation OR check_violation THEN
            -- جدولِ append-only (check_violation) یا هنوز وابستهٔ جدول دیگری (FK)؛ در حالت
            -- replica این‌ها رخ نمی‌دهد، در حالت بدون‌دسترسی این‌جا رد می‌شوند.
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
    -- تریگر animora_journal_capture روی DELETE از Tenants، برای ثبت ردیف 'D'، به ردیف
    -- TenantFeedState نیاز دارد؛ چون حلقهٔ بالا آن را پاک نمی‌کند، فقط مطمئن می‌شویم موجود است.
    INSERT INTO public."TenantFeedState" ("TenantId") VALUES (v_tenant) ON CONFLICT DO NOTHING;
    DELETE FROM public."Tenants"       WHERE "Id"       = v_tenant;
    -- باقی‌ماندهٔ فید و ژورنالِ همین مجموعه (ردیف‌های در انتظارِ تریگر تعویق‌افتاده هم با
    -- حذفشان کنار می‌روند، پس در commit خطای «Missing tenant feed state» نمی‌دهد).
    DELETE FROM public."TenantJournal"   WHERE "TenantId" = v_tenant;
    DELETE FROM public."TenantFeedState" WHERE "TenantId" = v_tenant;
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

  -- در حالت replica تریگر ژورنال اجرا نمی‌شود و ردیف فید ساخته نمی‌شود؛ صریح می‌سازیم تا
  -- نوشتن‌های بعدی برنامه با خطای «Missing tenant feed state» نخورند.
  INSERT INTO public."TenantFeedState" ("TenantId") VALUES (v_tenant) ON CONFLICT DO NOTHING;

  -- ───────── ۳) تعریف نقش‌های سیستمی ─────────
  -- بدون این پنج ردیف، PermissionEvaluator هیچ مجوزی نمی‌دهد (هیچ fallback به
  -- DefaultRolePermissions وجود ندارد): ورود موفق می‌شود ولی هر فرمانِ مجازشده
  -- با PERMISSION_DENIED رد می‌شود. محتوای این ماتریس آینهٔ دقیق
  -- Animora.Domain.Identity.DefaultRolePermissions است (۴۵/۱۶/۱۴/۱۲/۱۴ کد).
  INSERT INTO public."RoleDefinitions"
    ("Id","Role","DisplayKey","IsSystem","PermissionCodes","TenantId","UpdatedAt")
  SELECT pg_temp.animora_ulid(), v.role, v.display_key, true, v.codes, v_tenant, v_now
  FROM (VALUES
    (0, 'role.manager', ARRAY['patients.read','patients.write','patients.owner.edit','patients.pet.edit','reception.manage','visits.read','visits.write','visits.complete','vaccination.read','vaccination.write','appointments.read','appointments.book','appointments.move','appointments.cancel','boarding.read','boarding.manage','boarding.checkout','inventory.read','inventory.adjust','inventory.receive','purchasing.read','purchasing.manage','sales.read','sales.draft','sales.post','sales.void','sales.refund','sales.discount.approve','payments.register','payments.reverse','cashier.session.open','cashier.session.close','cheques.read','cheques.manage','finance.read','finance.post','finance.period.close','reports.read','settings.read','settings.write','users.manage','roles.manage','devices.manage','identity.staff.view','audit.read']::text[]),
    (1, 'role.doctor', ARRAY['patients.read','patients.write','visits.read','visits.write','visits.complete','vaccination.read','vaccination.write','appointments.read','appointments.book','appointments.move','boarding.read','inventory.read','sales.read','sales.draft','reports.read','settings.read']::text[]),
    (2, 'role.receptionist', ARRAY['patients.read','patients.write','reception.manage','appointments.read','appointments.book','appointments.move','appointments.cancel','visits.read','vaccination.read','boarding.read','boarding.manage','sales.read','sales.draft','settings.read']::text[]),
    (3, 'role.cashier', ARRAY['patients.read','sales.read','sales.draft','sales.post','payments.register','cashier.session.open','cashier.session.close','cheques.read','cheques.manage','inventory.read','reports.read','settings.read']::text[]),
    (4, 'role.accountant', ARRAY['finance.read','finance.post','finance.period.close','sales.read','payments.register','payments.reverse','cheques.read','cheques.manage','purchasing.read','purchasing.manage','inventory.read','reports.read','audit.read','settings.read']::text[])
  ) AS v(role, display_key, codes);
  RAISE NOTICE 'تعریف ۵ نقش سیستمی ساخته شد (Manager/Doctor/Receptionist/Cashier/Accountant).';

  -- ───────── ۴) Plan فعال standard ─────────
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
  v_max_dev := CASE WHEN v_max_ovr > 0 THEN v_max_ovr ELSE v_seat * v_dev_seat END;

  -- ───────── ۵) Subscription فعال + LicenseGrant فعال ─────────
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

  -- ───────── ۶) کاربر Manager فعال ─────────
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

  -- ───────── ۷) کاربر Doctor فعال (پیش‌شرط سناریوهای ویزیت/نوبت) ─────────
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

  -- ───────── ۸) تضمین «فعال و آمادهٔ ورود» بودن ─────────
  -- تور ایمنی: اگر ردیفی از اجرای قبلی یا نسخهٔ قدیمیِ اسکریپت با Status غیرفعال/قفل
  -- باقی مانده باشد، این‌جا به حالت سالم برگردانده می‌شود تا ورود تست هرگز به
  -- USER_DISABLED یا USER_LOCKED_OUT نخورد (رمزها دست‌نخورده می‌مانند).
  UPDATE public."Users"
     SET "Status" = 0, "FailedAttemptCount" = 0, "LockoutEndUtc" = NULL,
         "FirstFailureUtc" = NULL, "MustChangePassword" = false, "UpdatedAt" = v_now
   WHERE "TenantId" = v_tenant;

  UPDATE public."StaffMemberships"
     SET "Status" = 0, "EndedUtc" = NULL, "UpdatedAt" = v_now
   WHERE "TenantId" = v_tenant;
  RAISE NOTICE 'کاربران و عضویت‌ها «فعال و آمادهٔ ورود» تضمین شدند.';

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

# ---- خودآزمایی انتها-به-انتها: یک ورود واقعی روی همان سرور ----
# هدف: اسکریپت خودش ثابت کند محیط «قابل ورود» است، نه فقط اینکه SQL بی‌خطا اجرا شد.
# (باگ‌هایی مثل کد مجموعهٔ حروف‌بزرگ یا Status غیرفعال فقط این‌جا لو می‌روند.)
SMOKE_URL="${LIVETEST_SMOKE_URL:-http://127.0.0.1:5000}"
SMOKE_DEVICE="animora-setup-smoke"
if command -v curl >/dev/null 2>&1; then
  info "خودآزمایی ورود: POST ${SMOKE_URL}/auth/login …"
  SMOKE_BODY="$(python3 -c 'import json,sys; print(json.dumps({"tenantCode":sys.argv[1],"userName":sys.argv[2],"password":sys.argv[3],"deviceInstallationIdHash":sys.argv[4]}))' \
    "$TENANT_CODE" "$NEW_USER" "$NEW_PASSWORD" "$SMOKE_DEVICE")"
  SMOKE_TMP="$(mktemp)"
  SMOKE_CODE="$(curl -sS -m 20 -o "$SMOKE_TMP" -w '%{http_code}' \
      -H 'Content-Type: application/json' -d "$SMOKE_BODY" \
      "${SMOKE_URL}/auth/login" 2>/dev/null || echo 000)"
  if [ "$SMOKE_CODE" = "200" ] && grep -q '"accessToken"' "$SMOKE_TMP" 2>/dev/null; then
    ok "خودآزمایی ورود موفق بود (HTTP 200) — «${NEW_USER}» با کد مجموعهٔ «${TENANT_CODE}» وارد شد."
    # پاک‌کردن اثر خودآزمایی تا محیط دقیقاً همان چیزی بماند که seed ساخته است.
    PSQL -c "DELETE FROM public.\"RefreshTokens\" WHERE \"DeviceId\" IN (SELECT \"Id\" FROM public.\"Devices\" WHERE \"InstallationIdHash\" = '${SMOKE_DEVICE}');" >/dev/null 2>&1 || true
    PSQL -c "DELETE FROM public.\"Devices\" WHERE \"InstallationIdHash\" = '${SMOKE_DEVICE}';" >/dev/null 2>&1 || true
  else
    echo "  پاسخ سرور (HTTP ${SMOKE_CODE}):" >&2
    head -c 500 "$SMOKE_TMP" >&2 2>/dev/null; echo >&2
    rm -f "$SMOKE_TMP"
    die "خودآزمایی ورود شکست خورد. یعنی دادهٔ seed با سرورِ در حال اجرا هم‌خوان نیست (سرور بالا نیست؟ کد مجموعه/رمز؟)."
  fi
  rm -f "$SMOKE_TMP"
else
  info "curl نصب نیست؛ خودآزمایی ورود رد شد (اختیاری — ولی پیشنهاد می‌شود نصبش کنی)."
fi

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
