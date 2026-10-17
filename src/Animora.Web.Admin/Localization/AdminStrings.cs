namespace Animora.Web.Admin.Localization;

/// <summary>
/// Compact fa/en string table for the operator console (deliberately not <c>.resx</c>: the console stays dependency-free).
/// Keys are dotted and grouped by area. A missing key degrades to the key itself so a gap is visible, never a crash.
/// </summary>
public static class AdminStrings
{
    private static readonly Dictionary<string, (string En, string Fa)> Table = new(StringComparer.Ordinal)
    {
        // ── shell / navigation ──────────────────────────────────────────────────────────────────────────
        ["app.title"] = ("Animora operator console", "کنسول اپراتور انیمورا"),
        ["app.skip"] = ("Skip to content", "پرش به محتوا"),
        ["nav.main"] = ("Main navigation", "ناوبری اصلی"),
        ["nav.dashboard"] = ("Dashboard", "داشبورد"),
        ["nav.tenants"] = ("Tenants", "کلینیک‌ها"),
        ["nav.newTenant"] = ("New tenant", "کلینیک جدید"),
        ["nav.plans"] = ("Plans", "پلن‌ها"),
        ["nav.keys"] = ("Licence keys", "کلیدهای لایسنس"),
        ["nav.provisioning"] = ("Provisioning", "پروویژنینگ"),
        ["nav.websiteClients"] = ("Website clients", "کلاینت‌های وب‌سایت"),
        ["nav.mismatches"] = ("File mismatches", "ناهمخوانی فایل‌ها"),
        ["nav.audit"] = ("Audit", "ممیزی"),
        ["nav.system"] = ("System", "سیستم"),
        ["nav.logs"] = ("Logs", "لاگ‌ها"),
        ["nav.changePassword"] = ("Change password", "تغییر رمز عبور"),
        ["nav.signOut"] = ("Sign out", "خروج"),
        ["lang.switch"] = ("فارسی", "English"),
        ["lang.switchLabel"] = ("Switch to Persian", "تغییر زبان به انگلیسی"),

        // ── common ──────────────────────────────────────────────────────────────────────────────────────
        ["common.refresh"] = ("Refresh", "بازخوانی"),
        ["common.filter"] = ("Filter", "اعمال فیلتر"),
        ["common.search"] = ("Search", "جست‌وجو"),
        ["common.all"] = ("All", "همه"),
        ["common.previous"] = ("Previous", "قبلی"),
        ["common.next"] = ("Next", "بعدی"),
        ["common.page"] = ("Page", "صفحه"),
        ["common.pagination"] = ("Pagination", "صفحه‌بندی"),
        ["common.confirm"] = ("I confirm this action.", "این عملیات را تأیید می‌کنم."),
        ["common.status"] = ("Status", "وضعیت"),
        ["common.name"] = ("Name", "نام"),
        ["common.code"] = ("Code", "کد"),
        ["common.action"] = ("Action", "عملیات"),
        ["common.create"] = ("Create", "ایجاد"),
        ["common.none"] = ("Nothing to show.", "موردی برای نمایش نیست."),
        ["common.notFound"] = ("Page not found", "صفحه پیدا نشد"),
        ["common.back"] = ("Back", "بازگشت"),

        // ── login / account ─────────────────────────────────────────────────────────────────────────────
        ["login.title"] = ("Operator sign in", "ورود اپراتور"),
        ["login.username"] = ("Username", "نام کاربری"),
        ["login.password"] = ("Password", "رمز عبور"),
        ["login.submit"] = ("Sign in", "ورود"),
        ["pwd.title"] = ("Change operator password", "تغییر رمز عبور اپراتور"),
        ["pwd.current"] = ("Current password", "رمز عبور فعلی"),
        ["pwd.new"] = ("New password", "رمز عبور جدید"),
        ["pwd.submit"] = ("Change password and sign out", "تغییر رمز و خروج"),

        // ── dashboard ───────────────────────────────────────────────────────────────────────────────────
        ["dash.title"] = ("Dashboard", "داشبورد"),
        ["dash.version"] = ("Version", "نسخه"),
        ["dash.uptime"] = ("uptime", "مدت فعالیت"),
        ["dash.seconds"] = ("seconds", "ثانیه"),
        ["dash.readiness"] = ("Readiness", "آمادگی سرویس‌ها"),
        ["dash.probe"] = ("Probe", "بررسی"),
        ["dash.workers"] = ("Workers", "کارگرها"),
        ["dash.worker"] = ("Worker", "کارگر"),
        ["dash.lastSuccess"] = ("Last success age (s)", "زمان از آخرین موفقیت (ثانیه)"),
        ["dash.commands"] = ("Commands running", "فرمان‌های در حال اجرا"),
        ["dash.outbox"] = ("Outbox pending", "صف خروجی در انتظار"),
        ["dash.dead"] = ("Dead letters", "نامه‌های مرده"),
        ["dash.expiring"] = ("Expiring within 14 days", "انقضا تا ۱۴ روز آینده"),
        ["dash.pastDue"] = ("Past due / grace", "سررسید گذشته / مهلت"),
        ["dash.pg"] = ("PG active / pool max", "اتصال فعال / سقف پول"),
        ["dash.quick"] = ("Quick links", "دسترسی سریع"),
        ["dash.insight"] = ("Insight", "بینش (Insight)"),
        ["dash.enabled"] = ("enabled", "فعال"),
        ["dash.disabled"] = ("disabled", "غیرفعال"),
        ["dash.disabledNoAdr"] = ("disabled (no consent ADR)", "غیرفعال (بدون ADR رضایت)"),

        // ── tenants ─────────────────────────────────────────────────────────────────────────────────────
        ["tenants.title"] = ("Tenants", "کلینیک‌ها"),
        ["tenants.create"] = ("Create tenant", "ایجاد کلینیک"),
        ["tenants.search"] = ("Search code or name", "جست‌وجوی کد یا نام"),
        ["tenants.directory"] = ("Tenant directory", "فهرست کلینیک‌ها"),
        ["tenant.detail"] = ("Tenant detail", "جزئیات کلینیک"),
        ["tenant.tempPassword"] = ("Temporary password (shown once)", "رمز موقت (فقط یک‌بار نمایش داده می‌شود)"),
        ["tenant.seatsUsed"] = ("Seats used", "صندلی مصرف‌شده"),
        ["tenant.devices"] = ("devices", "دستگاه"),
        ["tenant.licences"] = ("licences", "لایسنس"),
        ["tenant.manageLicences"] = ("Manage licences", "مدیریت لایسنس‌ها"),
        ["tenant.subscription"] = ("Subscription", "اشتراک"),
        ["tenant.seats"] = ("seats", "صندلی"),
        ["tenant.ends"] = ("ends", "پایان"),
        ["tenant.events"] = ("Last 20 subscription events", "۲۰ رویداد اخیر اشتراک"),
        ["tenant.event"] = ("Event", "رویداد"),
        ["tenant.time"] = ("Time", "زمان"),
        ["tenant.staffLink"] = ("Staff accounts", "کارکنان و حساب‌ها"),
        ["tenant.productsLink"] = ("Products", "محصولات"),
        ["tenant.auditLink"] = ("Clinic audit & data", "ممیزی و داده‌های کلینیک"),
        ["new.title"] = ("Create tenant", "ایجاد کلینیک"),

        // ── other page titles ───────────────────────────────────────────────────────────────────────────
        ["audit.title"] = ("Audit viewer", "نمایشگر ممیزی"),
        ["plans.title"] = ("Plans", "پلن‌ها"),
        ["keys.title"] = ("Licence public keys", "کلیدهای عمومی لایسنس"),
        ["prov.title"] = ("Provisioning intents", "درخواست‌های پروویژنینگ"),
        ["web.title"] = ("Website clients", "کلاینت‌های وب‌سایت"),
        ["files.title"] = ("Flagged file mismatches", "فایل‌های دارای ناهمخوانی"),
        ["system.title"] = ("System status", "وضعیت سیستم"),
        ["licences.title"] = ("Tenant licences", "لایسنس‌های کلینیک"),

        // ── status panel ────────────────────────────────────────────────────────────────────────────────
        ["live.title"] = ("Live status (10-second cached refresh)", "وضعیت زنده (بازخوانی کش‌شده هر ۱۰ ثانیه)"),
        ["live.aria"] = ("Live server status", "وضعیت زندهٔ سرور"),

        ["staff.title"] = ("Staff accounts", "کارکنان و حساب‌ها"),
        ["staff.caption"] = ("Clinic staff roster", "فهرست کارکنان کلینیک"),
        ["staff.name"] = ("Name", "نام"),
        ["staff.username"] = ("Username", "نام کاربری"),
        ["staff.role"] = ("Role", "نقش"),
        ["staff.active"] = ("Active", "فعال"),
        ["staff.inactive"] = ("Disabled", "غیرفعال"),
        ["staff.devices"] = ("Devices", "دستگاه‌ها"),
        ["staff.search"] = ("Search name or username", "جست‌وجوی نام یا نام کاربری"),
        ["staff.allRoles"] = ("All roles", "همه نقش‌ها"),
        ["staff.resetPassword"] = ("Reset password", "ریست رمز عبور"),
        ["staff.resetHint"] = ("A new temporary password is generated and shown once. The user's active sessions end immediately.", "رمز موقت جدید ساخته و فقط یک‌بار نمایش داده می‌شود. نشست‌های فعال کاربر بلافاصله پایان می‌یابد."),
        ["staff.create"] = ("Create staff account", "ساخت حساب کارمند"),
        ["staff.createName"] = ("Full name", "نام و نام خانوادگی"),
        ["staff.createUser"] = ("Username", "نام کاربری"),
        ["staff.createRole"] = ("Role", "نقش"),
        ["staff.createSubmit"] = ("Create account", "ساخت حساب"),
        ["staff.created"] = ("Account created", "حساب ساخته شد"),
        ["staff.tempPassword"] = ("Temporary password (shown once)", "رمز موقت (فقط یک‌بار نمایش داده می‌شود)"),
        ["staff.backToTenant"] = ("Back to clinic", "بازگشت به کلینیک"),
        ["role.Manager"] = ("Manager", "مدیر"),
        ["role.Doctor"] = ("Doctor", "دامپزشک"),
        ["role.Receptionist"] = ("Receptionist", "پذیرش"),
        ["role.Cashier"] = ("Cashier", "صندوق‌دار"),
        ["role.Accountant"] = ("Accountant", "حسابدار"),
    };

    /// <summary>Resolves <paramref name="key"/> for <paramref name="lang"/> (<c>fa</c>/<c>en</c>); unknown keys return the key.</summary>
    public static string Get(string key, string lang) =>
        Table.TryGetValue(key, out var pair) ? (lang == AdminCulture.En ? pair.En : pair.Fa) : key;

    /// <summary>Every registered key (for the parity test).</summary>
    public static IEnumerable<string> Keys => Table.Keys;

    /// <summary>The registered pair for <paramref name="key"/>, or <see langword="null"/>.</summary>
    public static (string En, string Fa)? Pair(string key) => Table.TryGetValue(key, out var pair) ? pair : null;
}
