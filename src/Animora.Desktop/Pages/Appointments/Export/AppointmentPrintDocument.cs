using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using Animora.Contracts.Appointments;
using Animora.Desktop.Localization;

namespace Animora.Desktop.Pages.Appointments.Export;

/// <summary>
/// Builds the self-contained, print-ready HTML for the appointments export.
/// Pure function of <see cref="AppointmentPrintOptions"/>: no I/O, no UI types.
///
/// Print rules baked in:
/// • <c>@page</c> carries the exact paper (A4/A3 × portrait/landscape) and the
///   on-screen preview renders each sheet at that same physical width, so what
///   you see is what prints.
/// • Each day is its own section; table <c>thead</c> repeats (with the day
///   label) on every continuation page, rows never split across pages and a day
///   header is never orphaned at a page bottom.
/// • Single-doctor prints promote the doctor to the document header and drop the
///   doctor column; multi-doctor prints add a doctor column (timeline) or one
///   sub-table per doctor (by-doctor layout).
/// </summary>
public static class AppointmentPrintDocument
{
    public static string Build(AppointmentPrintOptions o, string fontCss)
    {
        var doctorById = o.Doctors.ToDictionary(d => d.Id);
        var byDay = o.Appointments
            .GroupBy(a => a.Date)
            .ToDictionary(g => g.Key, g => g.OrderBy(a => a.StartMinutes).ThenBy(a => a.PetName, StringComparer.Ordinal).ToList());

        var days = o.Days.Where(d => o.IncludeEmptyDays || byDay.ContainsKey(d)).OrderBy(d => d).ToList();
        var singleDoctor = o.Doctors.Count == 1 ? o.Doctors[0] : null;
        var multiDoctor = singleDoctor is null;

        var sb = new StringBuilder(64 * 1024);
        sb.Append("<!doctype html><html lang=\"fa\" dir=\"rtl\"><head><meta charset=\"utf-8\">");
        sb.Append("<title>").Append(E(o.Title)).Append(" — ").Append(E(RangeText(o.Days))).Append("</title>");
        sb.Append("<style>").Append(fontCss).Append(Css(o)).Append("</style></head><body>");

        // Screen-only toolbar (hidden in print).
        sb.Append("<div class=\"bar no-print\"><div><b>پیش‌نمایش چاپ</b><span>")
          .Append(E(PaperLabel(o))).Append(" · ").Append(Fa(o.Appointments.Count)).Append(" نوبت · ")
          .Append(Fa(days.Count)).Append(" روز</span></div>")
          .Append("<button onclick=\"window.print()\">چاپ / ذخیره PDF</button></div>");

        sb.Append("<main class=\"sheet\">");
        AppendHeader(sb, o, singleDoctor);

        if (o.IncludeSummary)
        {
            AppendSummary(sb, o, byDay, days, doctorById);
        }

        if (days.Count == 0)
        {
            sb.Append("<div class=\"empty\">در بازهٔ انتخاب‌شده نوبتی مطابق فیلترها یافت نشد.</div>");
        }

        for (var i = 0; i < days.Count; i++)
        {
            var day = days[i];
            var list = byDay.TryGetValue(day, out var l) ? l : [];
            var breakBefore = o.NewPagePerDay && i > 0;
            AppendDay(sb, o, day, list, multiDoctor, doctorById, breakBefore);
        }

        AppendFooter(sb, o);
        sb.Append("</main>");

        if (o.AutoPrint)
        {
            // Fonts are embedded as data URIs; wait for them so the first print isn't fallback-rendered.
            sb.Append("<script>window.addEventListener('load',function(){(document.fonts?document.fonts.ready:Promise.resolve()).then(function(){setTimeout(function(){window.print();},250);});});</script>");
        }

        sb.Append("</body></html>");
        return sb.ToString();
    }

    // ── header ───────────────────────────────────────────────────────────────
    private static void AppendHeader(StringBuilder sb, AppointmentPrintOptions o, AppointmentDoctor? single)
    {
        var clinic = string.IsNullOrWhiteSpace(o.ClinicName) ? "کلینیک دامپزشکی" : o.ClinicName;
        sb.Append("<header class=\"doc-head\"><div class=\"brand\"><div class=\"logo\">")
          .Append(E(Initial(clinic))).Append("</div><div><div class=\"clinic\">").Append(E(clinic))
          .Append("</div><div class=\"doc-title\">").Append(E(o.Title)).Append("</div></div></div>");

        sb.Append("<div class=\"meta\">");
        Meta(sb, "بازهٔ زمانی", RangeText(o.Days));
        if (single is not null)
        {
            var who = string.IsNullOrWhiteSpace(single.Specialty) ? single.Name : $"{single.Name} · {single.Specialty}";
            Meta(sb, "پزشک", who);
        }
        else
        {
            Meta(sb, "پزشکان", o.AllDoctors ? "همه پزشکان" : string.Join("، ", o.Doctors.Select(d => d.ShortName.Length > 0 ? d.ShortName : d.Name)));
        }

        Meta(sb, "تاریخ تهیه", NowText());
        if (!string.IsNullOrWhiteSpace(o.PreparedBy))
        {
            Meta(sb, "تهیه‌کننده", o.PreparedBy);
        }

        sb.Append("</div></header>");
    }

    private static void Meta(StringBuilder sb, string k, string v) =>
        sb.Append("<div class=\"m\"><span>").Append(E(k)).Append("</span><b>").Append(E(v)).Append("</b></div>");

    // ── summary ──────────────────────────────────────────────────────────────
    private static void AppendSummary(
        StringBuilder sb,
        AppointmentPrintOptions o,
        Dictionary<DateOnly, List<AppointmentEvent>> byDay,
        List<DateOnly> days,
        Dictionary<string, AppointmentDoctor> doctorById)
    {
        var all = o.Appointments;
        int Count(AppointmentLifecycle s) => all.Count(a => a.Lifecycle == s);

        sb.Append("<section class=\"summary\"><div class=\"kpis\">");
        Kpi(sb, "کل نوبت‌ها", all.Count, "brand");
        Kpi(sb, "روزهای دارای نوبت", byDay.Count, "ink");
        Kpi(sb, "تأیید شده", Count(AppointmentLifecycle.Confirmed), "info");
        Kpi(sb, "در انتظار تأیید", Count(AppointmentLifecycle.Scheduled), "warn");
        Kpi(sb, "پذیرش شده", all.Count(a => a.IsCheckedIn), "ok");
        Kpi(sb, "عدم مراجعه", Count(AppointmentLifecycle.NoShow), "danger");
        Kpi(sb, "لغو شده", Count(AppointmentLifecycle.Cancelled), "danger");
        sb.Append("</div>");

        var perDoctor = all.GroupBy(a => a.DoctorId).Select(g => (Name: DoctorName(doctorById, g.Key), N: g.Count()))
            .OrderByDescending(x => x.N).ToList();
        var perType = all.GroupBy(a => a.TypeId).Select(g => (Type: o.Types.GetValueOrDefault(g.Key), Id: g.Key, N: g.Count()))
            .OrderByDescending(x => x.N).ToList();

        if (perDoctor.Count > 1 || perType.Count > 0)
        {
            sb.Append("<div class=\"breakdown\">");
            if (perDoctor.Count > 1)
            {
                sb.Append("<div class=\"bd\"><h4>به تفکیک پزشک</h4><div class=\"tags\">");
                foreach (var (name, n) in perDoctor)
                {
                    sb.Append("<span class=\"tag\">").Append(E(name)).Append("<i>").Append(Fa(n)).Append("</i></span>");
                }

                sb.Append("</div></div>");
            }

            if (perType.Count > 0)
            {
                sb.Append("<div class=\"bd\"><h4>به تفکیک نوع نوبت</h4><div class=\"tags\">");
                foreach (var (type, id, n) in perType)
                {
                    sb.Append("<span class=\"tag\"><em style=\"background:").Append(Color(type?.Accent)).Append("\"></em>")
                      .Append(E(type?.Label ?? id)).Append("<i>").Append(Fa(n)).Append("</i></span>");
                }

                sb.Append("</div></div>");
            }

            sb.Append("</div>");
        }

        // A compact per-day index only makes sense for multi-day prints.
        if (days.Count > 1)
        {
            sb.Append("<div class=\"index\">");
            foreach (var d in days)
            {
                var n = byDay.TryGetValue(d, out var l) ? l.Count : 0;
                sb.Append("<span class=\"ix").Append(n == 0 ? " zero" : string.Empty).Append("\"><b>")
                  .Append(E(WeekdayName(d))).Append("</b> ").Append(E(AppointmentsCalendarMath.Medium(d)))
                  .Append("<i>").Append(Fa(n)).Append("</i></span>");
            }

            sb.Append("</div>");
        }

        sb.Append("</section>");
    }

    private static void Kpi(StringBuilder sb, string label, int value, string tone) =>
        sb.Append("<div class=\"kpi ").Append(tone).Append("\"><b>").Append(Fa(value)).Append("</b><span>")
          .Append(E(label)).Append("</span></div>");

    // ── one day ──────────────────────────────────────────────────────────────
    private static void AppendDay(
        StringBuilder sb,
        AppointmentPrintOptions o,
        DateOnly day,
        List<AppointmentEvent> list,
        bool multiDoctor,
        Dictionary<string, AppointmentDoctor> doctorById,
        bool breakBefore)
    {
        var shortLabel = $"{WeekdayName(day)} {AppointmentsCalendarMath.Medium(day)}";
        sb.Append("<section class=\"day").Append(breakBefore ? " pb" : string.Empty).Append("\">");
        var head = new StringBuilder();
        head.Append("<div class=\"day-head\"><div class=\"dh-date\"><span class=\"dh-num\">")
          .Append(E(AppointmentsCalendarMath.DayNumber(day))).Append("</span><div><b>").Append(E(WeekdayName(day)))
          .Append("</b><span>").Append(E(AppointmentsCalendarMath.MonthName(day))).Append(' ')
          .Append(E(AppointmentsCalendarMath.YearText(day))).Append(" · ").Append(E(JalaliShort(day)))
          .Append("</span></div></div><div class=\"dh-stats\">");

        head.Append("<span class=\"pill strong\">").Append(Fa(list.Count)).Append(" نوبت</span>");
        if (!o.DayOnly && list.Count > 0)
        {
            head.Append("<span class=\"pill\">").Append(E(AppointmentsPresentation.Time(list.Min(a => a.StartMinutes))))
              .Append(" تا ").Append(E(AppointmentsPresentation.Time(list.Max(a => a.EndMinutes)))).Append("</span>");
        }

        var cancelled = list.Count(a => a.Lifecycle == AppointmentLifecycle.Cancelled);
        if (cancelled > 0)
        {
            head.Append("<span class=\"pill danger\">").Append(Fa(cancelled)).Append(" لغو</span>");
        }

        head.Append("</div></div>");

        var multiTable = o.Layout == PrintLayout.ByDoctor && multiDoctor;
        if (list.Count == 0 || multiTable)
        {
            sb.Append(head);
        }

        if (list.Count == 0)
        {
            sb.Append("<div class=\"day-empty\">برای این روز نوبتی ثبت نشده است.</div></section>");
            return;
        }

        if (multiTable)
        {
            var order = o.Doctors.Select((d, i) => (d.Id, i)).ToDictionary(x => x.Id, x => x.i);
            foreach (var group in list.GroupBy(a => a.DoctorId).OrderBy(g => order.GetValueOrDefault(g.Key, int.MaxValue)))
            {
                var doc = doctorById.GetValueOrDefault(group.Key);
                var dg = new StringBuilder();
                dg.Append("<div class=\"dg-head\"><em style=\"background:")
                  .Append(Color(doc?.Accent)).Append("\"></em><b>").Append(E(doc?.Name ?? "بدون پزشک")).Append("</b>");
                if (!string.IsNullOrWhiteSpace(doc?.Specialty))
                {
                    dg.Append("<span>").Append(E(doc!.Specialty)).Append("</span>");
                }

                dg.Append("<i>").Append(E(shortLabel)).Append(" · ").Append(Fa(group.Count())).Append(" نوبت</i></div>");
                sb.Append("<div class=\"doc-group\">");
                AppendTable(sb, o, group.ToList(), showDoctor: false, doctorById, dg.ToString());
                sb.Append("</div>");
            }
        }
        else
        {
            AppendTable(sb, o, list, showDoctor: multiDoctor, doctorById, head.ToString());
        }

        sb.Append("</section>");
    }

    private static void AppendTable(
        StringBuilder sb,
        AppointmentPrintOptions o,
        List<AppointmentEvent> rows,
        bool showDoctor,
        Dictionary<string, AppointmentDoctor> doctorById,
        string captionHtml)
    {
        var cols = 2 /* # + patient */ + (o.ShowCheckbox ? 1 : 0) + (o.DayOnly ? 0 : 1) + (showDoctor ? 1 : 0)
                   + 1 /* owner */ + (o.ShowType ? 1 : 0) + (o.ShowStatus ? 1 : 0) + (o.ShowCode ? 1 : 0)
                   + (o.ShowNotesColumn ? 1 : 0);

        sb.Append("<table class=\"grid\"><thead>");
        // The day (or doctor) header lives inside thead so the browser repeats it, with the
        // column labels, at the top of every continuation page of a long day.
        sb.Append("<tr class=\"cap\"><th colspan=\"").Append(cols).Append("\">").Append(captionHtml)
          .Append("</th></tr><tr class=\"cols\">");
        if (o.ShowCheckbox)
        {
            sb.Append("<th class=\"c-chk\">✓</th>");
        }

        sb.Append("<th class=\"c-n\">#</th>");
        if (!o.DayOnly)
        {
            sb.Append("<th class=\"c-time\">ساعت</th>");
        }

        sb.Append("<th class=\"c-pet\">بیمار</th><th class=\"c-owner\">صاحب").Append(o.ShowPhone ? " / تماس" : string.Empty).Append("</th>");
        if (showDoctor)
        {
            sb.Append("<th class=\"c-doc\">پزشک</th>");
        }

        if (o.ShowType)
        {
            sb.Append("<th class=\"c-type\">نوع</th>");
        }

        if (o.ShowStatus)
        {
            sb.Append("<th class=\"c-st\">وضعیت</th>");
        }

        if (o.ShowCode)
        {
            sb.Append("<th class=\"c-code\">کد</th>");
        }

        if (o.ShowNotesColumn)
        {
            sb.Append("<th class=\"c-memo\">یادداشت</th>");
        }

        sb.Append("</tr></thead><tbody>");

        for (var i = 0; i < rows.Count; i++)
        {
            var a = rows[i];
            var type = o.Types.GetValueOrDefault(a.TypeId);
            var cancelled = a.Lifecycle == AppointmentLifecycle.Cancelled;
            sb.Append("<tr").Append(cancelled ? " class=\"x\"" : string.Empty).Append('>');
            if (o.ShowCheckbox)
            {
                sb.Append("<td class=\"c-chk\"><span class=\"box\"></span></td>");
            }

            sb.Append("<td class=\"c-n\">").Append(Fa(i + 1)).Append("</td>");
            if (!o.DayOnly)
            {
                sb.Append("<td class=\"c-time\"><b>").Append(E(AppointmentsPresentation.Time(a.StartMinutes)))
                  .Append("</b><small>").Append(E(AppointmentsPresentation.Time(a.EndMinutes))).Append(" · ")
                  .Append(Fa(a.DurationMinutes)).Append("′</small></td>");
            }

            sb.Append("<td class=\"c-pet\"><b>").Append(E(a.PetName)).Append("</b>");
            if (!string.IsNullOrWhiteSpace(a.PetKind))
            {
                sb.Append("<small>").Append(E(a.PetKind)).Append("</small>");
            }

            if (o.ShowNote && !string.IsNullOrWhiteSpace(a.Note))
            {
                sb.Append("<div class=\"note\">").Append(E(a.Note)).Append("</div>");
            }

            sb.Append("</td><td class=\"c-owner\"><b>").Append(E(a.OwnerName)).Append("</b>");
            if (o.ShowPhone && !string.IsNullOrWhiteSpace(a.OwnerPhone))
            {
                sb.Append("<small class=\"ltr\">").Append(E(Fa(a.OwnerPhone))).Append("</small>");
            }

            sb.Append("</td>");
            if (showDoctor)
            {
                var doc = doctorById.GetValueOrDefault(a.DoctorId);
                sb.Append("<td class=\"c-doc\"><em style=\"background:").Append(Color(doc?.Accent)).Append("\"></em>")
                  .Append(E(doc is null ? "—" : (doc.ShortName.Length > 0 ? doc.ShortName : doc.Name))).Append("</td>");
            }

            if (o.ShowType)
            {
                sb.Append("<td class=\"c-type\"><span class=\"type\" style=\"border-color:").Append(Color(type?.Accent))
                  .Append("\"><em style=\"background:").Append(Color(type?.Accent)).Append("\"></em>")
                  .Append(E(type?.Label ?? a.TypeId)).Append("</span></td>");
            }

            if (o.ShowStatus)
            {
                sb.Append("<td class=\"c-st\"><span class=\"st ").Append(AppointmentsPresentation.LifecycleClassKey(a.Lifecycle))
                  .Append("\">").Append(E(AppointmentsPresentation.Lifecycle(a.Lifecycle).Label)).Append("</span></td>");
            }

            if (o.ShowCode)
            {
                sb.Append("<td class=\"c-code ltr\">").Append(E(Fa(a.Code))).Append("</td>");
            }

            if (o.ShowNotesColumn)
            {
                sb.Append("<td class=\"c-memo\"></td>");
            }

            sb.Append("</tr>");
        }

        sb.Append("</tbody></table>");
    }

    private static void AppendFooter(StringBuilder sb, AppointmentPrintOptions o)
    {
        sb.Append("<footer class=\"doc-foot\"><span>").Append(E(string.IsNullOrWhiteSpace(o.ClinicName) ? "Animora" : o.ClinicName))
          .Append(" · ").Append(E(o.Title)).Append("</span><span>تهیه شده در ").Append(E(NowText()))
          .Append("</span><span class=\"sign\">امضا / مهر: </span></footer>");
    }

    // ── CSS ──────────────────────────────────────────────────────────────────
    private static string Css(AppointmentPrintOptions o)
    {
        var (w, h) = o.Paper == PrintPaper.A3 ? (297, 420) : (210, 297);
        if (o.Landscape)
        {
            (w, h) = (h, w);
        }

        // Type scale follows the sheet: A3 is √2 larger than A4, so text grows ~1.2× (not 1.41×)
        // to gain more rows per page while staying legible; compact trims another ~10%.
        var basePt = (o.Paper == PrintPaper.A3 ? 11.5 : 9.6) * (o.Density == PrintDensity.Compact ? 0.9 : 1.0);
        var padY = o.Density == PrintDensity.Compact ? 1.2 : 2.1;
        var margin = o.Paper == PrintPaper.A3 ? 14 : 10;
        var memoW = o.Landscape || o.Paper == PrintPaper.A3 ? 42 : 30;

        var css = new StringBuilder();
        css.Append(CultureInfo.InvariantCulture, $"@page{{size:{o.Paper} {(o.Landscape ? "landscape" : "portrait")};margin:{margin}mm {margin}mm {margin + 4}mm;");
        css.Append("@bottom-center{content:'صفحه ' counter(page) ' از ' counter(pages);font:8pt Vazirmatn,Tahoma,sans-serif;color:#667085;}}");
        css.Append(CultureInfo.InvariantCulture, $":root{{--w:{w}mm;--h:{h}mm;--m:{margin}mm;--fs:{basePt:0.##}pt;--py:{padY:0.#}mm;--memo:{memoW}mm;}}");
        css.Append(StaticCss);
        return css.ToString();
    }

    private const string StaticCss = """
*{box-sizing:border-box;margin:0;padding:0}
html{-webkit-print-color-adjust:exact;print-color-adjust:exact}
body{font-family:Vazirmatn,Tahoma,sans-serif;font-size:var(--fs);color:#101828;background:#e9edf1;line-height:1.55}
.ltr{direction:ltr;unicode-bidi:isolate;display:inline-block}
.bar{position:sticky;top:0;z-index:5;display:flex;justify-content:space-between;align-items:center;gap:16px;padding:10px 22px;background:#101828;color:#fff;font-size:13px}
.bar span{opacity:.7;margin-inline-start:10px;font-size:12px}
.bar button{font:inherit;font-weight:700;background:#14b8a6;color:#fff;border:0;border-radius:9px;padding:8px 18px;cursor:pointer}
.sheet{width:var(--w);min-height:var(--h);margin:22px auto;background:#fff;padding:var(--m);box-shadow:0 10px 40px rgba(16,24,40,.14)}
.doc-head{display:flex;justify-content:space-between;align-items:flex-end;gap:6mm;padding-bottom:3.5mm;border-bottom:.6mm solid #0d9488;margin-bottom:4mm}
.brand{display:flex;align-items:center;gap:3mm}
.logo{width:11mm;height:11mm;border-radius:3mm;background:linear-gradient(135deg,#14b8a6,#0f766e);color:#fff;font-weight:800;font-size:1.7em;display:flex;align-items:center;justify-content:center}
.clinic{font-size:1.55em;font-weight:800;line-height:1.25}
.doc-title{color:#0f766e;font-weight:700;font-size:1.05em}
.meta{display:flex;flex-wrap:wrap;gap:2mm 5mm;justify-content:flex-end;max-width:62%}
.m{display:flex;flex-direction:column;align-items:flex-start;min-width:22mm}
.m span{font-size:.78em;color:#667085}
.m b{font-weight:700;font-size:.95em}
.summary{border:.25mm solid #e4e7ec;border-radius:2.5mm;padding:3mm;margin-bottom:5mm;break-inside:avoid}
.kpis{display:grid;grid-template-columns:repeat(6,1fr);gap:2mm}
.kpi{border-radius:2mm;padding:1.8mm 2.5mm;background:#f8fafc;border-inline-start:.9mm solid #98a2b3}
.kpi b{display:block;font-size:1.45em;font-weight:800;line-height:1.2}
.kpi span{font-size:.8em;color:#475467}
.kpi.brand{border-color:#0d9488;background:#f0fdfa}.kpi.info{border-color:#2e90fa}.kpi.warn{border-color:#f79009}.kpi.ok{border-color:#12b76a}.kpi.danger{border-color:#f04438}.kpi.ink{border-color:#475467}
.breakdown{display:flex;gap:5mm;margin-top:3mm;flex-wrap:wrap}
.bd{flex:1;min-width:60mm}
.bd h4{font-size:.8em;color:#667085;font-weight:700;margin-bottom:1.2mm}
.tags{display:flex;flex-wrap:wrap;gap:1.4mm}
.tag{display:inline-flex;align-items:center;gap:1.3mm;border:.25mm solid #e4e7ec;border-radius:99px;padding:.4mm 2.4mm;font-size:.85em}
.tag i{font-style:normal;font-weight:800;color:#0f766e}
.tag em,.c-doc em,.type em,.dg-head em{display:inline-block;width:2mm;height:2mm;border-radius:50%;flex:none}
.index{display:flex;flex-wrap:wrap;gap:1.4mm;margin-top:3mm;padding-top:2.5mm;border-top:.25mm dashed #e4e7ec}
.ix{font-size:.8em;background:#f8fafc;border-radius:1.5mm;padding:.6mm 2mm;display:inline-flex;gap:1.2mm;align-items:center}
.ix i{font-style:normal;font-weight:800;background:#0d9488;color:#fff;border-radius:99px;padding:0 1.6mm;font-size:.95em}
.ix.zero{opacity:.55}.ix.zero i{background:#98a2b3}
.day{margin-bottom:5mm}
.day.pb{break-before:page}
.day-head{display:flex;justify-content:space-between;align-items:center;gap:4mm;padding:1.8mm 0 1.8mm;margin-bottom:1.6mm;border-bottom:.35mm solid #101828;break-after:avoid;break-inside:avoid}
.dh-date{display:flex;align-items:center;gap:2.5mm}
.dh-num{font-size:2.1em;font-weight:800;line-height:1;color:#0d9488;min-width:9mm;text-align:center}
.dh-date b{display:block;font-size:1.15em;font-weight:800;line-height:1.2}
.dh-date span{font-size:.85em;color:#667085}
.dh-stats{display:flex;gap:1.5mm;flex-wrap:wrap;justify-content:flex-end}
.pill{font-size:.82em;border:.25mm solid #e4e7ec;border-radius:99px;padding:.3mm 2.4mm;color:#344054}
.pill.strong{background:#101828;color:#fff;border-color:#101828;font-weight:700}
.pill.danger{color:#b42318;border-color:#fecdca;background:#fef3f2}
.day-empty,.empty{color:#98a2b3;font-size:.9em;padding:3mm;text-align:center;border:.25mm dashed #e4e7ec;border-radius:2mm}
.empty{margin-top:10mm;font-size:1.1em}
.doc-group{margin:2.5mm 0 3.5mm}
.dg-head{display:flex;align-items:center;gap:1.8mm;padding:1mm 2mm;background:#f8fafc;border-radius:1.5mm;margin-bottom:1mm;break-after:avoid}
.dg-head b{font-weight:800}.dg-head span{color:#667085;font-size:.85em}
.dg-head i{margin-inline-start:auto;font-style:normal;font-weight:700;font-size:.85em;color:#0f766e}
table.grid{width:100%;border-collapse:collapse;table-layout:auto}
.grid thead{display:table-header-group}
.grid tr{break-inside:avoid}
.grid tr.cap th{text-align:start;background:#fff;border:0;padding:0;font-size:1em;color:inherit;font-weight:400;white-space:normal}
.grid tr.cols th{font-size:.78em;font-weight:700;color:#475467;text-align:start;padding:1.2mm 1.6mm;background:#f2f4f7;border-bottom:.25mm solid #d0d5dd;white-space:nowrap}
.grid td{padding:var(--py) 1.6mm;border-bottom:.2mm solid #eaecf0;vertical-align:top}
.grid tbody tr:nth-child(even) td{background:#fcfcfd}
.grid td b{font-weight:700}
.grid td small{display:block;color:#667085;font-size:.82em;line-height:1.35}
.c-chk{width:7mm;text-align:center}
.box{display:inline-block;width:3.6mm;height:3.6mm;border:.35mm solid #475467;border-radius:.7mm;margin-top:.4mm}
.c-n{width:6mm;color:#98a2b3;text-align:center}
.c-time{width:15mm;white-space:nowrap}
.c-time b{font-size:1.08em}
.c-doc,.c-type,.c-st,.c-code{white-space:nowrap}
.c-doc em{margin-inline-end:1.2mm;vertical-align:middle}
.type{display:inline-flex;align-items:center;gap:1.2mm;border:.25mm solid;border-radius:99px;padding:0 2mm;font-size:.86em}
.st{font-size:.82em;font-weight:700;border-radius:99px;padding:.15mm 2mm;display:inline-block}
.st.confirmed{background:#eff8ff;color:#175cd3}.st.pending{background:#fffaeb;color:#b54708}.st.done,.st.checkedin{background:#ecfdf3;color:#067647}.st.invisit{background:#eef4ff;color:#3538cd}.st.noshow{background:#fef3f2;color:#b42318}.st.cancel{background:#fef3f2;color:#b42318}
.note{margin-top:.6mm;font-size:.82em;color:#475467;background:#f9fafb;border-inline-start:.6mm solid #d0d5dd;padding:.3mm 1.6mm;border-radius:.6mm}
.c-memo{width:var(--memo);border-inline-start:.2mm dashed #d0d5dd}
.c-code{font-size:.85em;color:#475467}
tr.x td{color:#98a2b3}
tr.x .c-pet b,tr.x .c-owner b{text-decoration:line-through}
.doc-foot{display:flex;justify-content:space-between;gap:4mm;margin-top:6mm;padding-top:2mm;border-top:.25mm solid #e4e7ec;color:#667085;font-size:.8em;break-inside:avoid}
.doc-foot .sign{min-width:45mm;border-bottom:.25mm dotted #98a2b3}
@media print{
  body{background:#fff}
  .no-print{display:none!important}
  .sheet{width:auto;min-height:0;margin:0;padding:0;box-shadow:none}
}
@media screen and (max-width:900px){.sheet{width:auto;margin:10px}}
""";

    // ── helpers ──────────────────────────────────────────────────────────────
    private static readonly string[] WeekdayNames = ["شنبه", "یکشنبه", "دوشنبه", "سه‌شنبه", "چهارشنبه", "پنجشنبه", "جمعه"];

    public static string WeekdayName(DateOnly d) => WeekdayNames[AppointmentsCalendarMath.WeekIndex(d)];

    private static string JalaliShort(DateOnly d) => JalaliDate.FromDateTime(d.ToDateTime(TimeOnly.MinValue)).ToString("short");

    public static string RangeText(IReadOnlyList<DateOnly> days)
    {
        if (days.Count == 0)
        {
            return "—";
        }

        var first = days.Min();
        var last = days.Max();
        if (first == last)
        {
            return $"{WeekdayName(first)} {AppointmentsCalendarMath.Full(first)}";
        }

        var span = $"{AppointmentsCalendarMath.Full(first)} تا {AppointmentsCalendarMath.Full(last)}";
        var contiguous = last.DayNumber - first.DayNumber + 1 == days.Count;
        return contiguous ? span : $"{Fa(days.Count)} روز منتخب ({span})";
    }

    private static string NowText()
    {
        var now = DateTime.Now;
        return $"{JalaliDate.FromDateTime(now).ToString("short")} · {Fa(now.ToString("HH:mm", CultureInfo.InvariantCulture))}";
    }

    private static string PaperLabel(AppointmentPrintOptions o) => $"{o.Paper} {(o.Landscape ? "افقی" : "عمودی")}";

    private static string DoctorName(Dictionary<string, AppointmentDoctor> map, string id) =>
        map.TryGetValue(id, out var d) ? d.Name : "بدون پزشک";

    private static string Initial(string s) => s.Trim().Length > 0 ? s.Trim()[..1] : "A";

    private static string Color(string? hex) =>
        !string.IsNullOrWhiteSpace(hex) && hex.Length <= 9 && hex.StartsWith('#') && hex.Skip(1).All(Uri.IsHexDigit)
            ? hex
            : "#98a2b3";

    private static string Fa(long n) => PersianNumberFormatter.ToPersianDigits(n);

    private static string Fa(string s) => PersianNumberFormatter.ToPersianDigits(s);

    private static string E(string? s) => WebUtility.HtmlEncode(s ?? string.Empty);
}
