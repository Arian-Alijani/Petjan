using System.Net;
using System.Text;
using Animora.Desktop.Localization;

namespace Animora.Desktop.Services;

/// <summary>One labelled header fact of a printed document (customer, date, …).</summary>
public sealed record PrintMeta(string Label, string Value);

/// <summary>A total line under the table; <paramref name="Strong"/> emphasises the grand total.</summary>
public sealed record PrintTotal(string Label, string Value, bool Strong = false);

/// <summary>
/// Builds a minimal, self-contained, RTL print-ready HTML document (title, meta strip, one table, totals).
/// Pure function of its arguments — no I/O and no UI types — so every screen's receipt / invoice / day report
/// shares one look and goes through <see cref="IPrintService"/> (the browser's print dialog handles printer /
/// «Save as PDF»). Screens pass already-formatted Persian text; this class only escapes and lays it out.
/// </summary>
public static class PrintDocumentBuilder
{
    public static string Build(
        string title,
        string subtitle,
        IReadOnlyList<PrintMeta> meta,
        IReadOnlyList<string> columns,
        IReadOnlyList<IReadOnlyList<string>> rows,
        IReadOnlyList<PrintTotal> totals,
        string fontCss,
        string? footnote = null)
    {
        var sb = new StringBuilder(8 * 1024);
        sb.Append("<!doctype html><html lang=\"fa\" dir=\"rtl\"><head><meta charset=\"utf-8\"><title>")
          .Append(E(title)).Append("</title><style>").Append(fontCss).Append(Css).Append("</style></head><body>");

        sb.Append("<div class=\"bar no-print\"><b>پیش‌نمایش چاپ</b>")
          .Append("<button onclick=\"window.print()\">چاپ / ذخیره PDF</button></div>");

        sb.Append("<main class=\"sheet\"><header><h1>").Append(E(title)).Append("</h1>");
        if (subtitle.Length > 0)
        {
            sb.Append("<p class=\"sub\">").Append(E(subtitle)).Append("</p>");
        }

        sb.Append("</header>");

        if (meta.Count > 0)
        {
            sb.Append("<section class=\"meta\">");
            foreach (var m in meta)
            {
                sb.Append("<div><span>").Append(E(m.Label)).Append("</span><b>").Append(E(m.Value)).Append("</b></div>");
            }

            sb.Append("</section>");
        }

        if (columns.Count > 0)
        {
            sb.Append("<table><thead><tr>");
            foreach (var c in columns)
            {
                sb.Append("<th>").Append(E(c)).Append("</th>");
            }

            sb.Append("</tr></thead><tbody>");
            if (rows.Count == 0)
            {
                sb.Append("<tr><td class=\"empty\" colspan=\"").Append(columns.Count).Append("\">موردی برای نمایش وجود ندارد</td></tr>");
            }

            foreach (var row in rows)
            {
                sb.Append("<tr>");
                for (var i = 0; i < columns.Count; i++)
                {
                    sb.Append("<td>").Append(E(i < row.Count ? row[i] : string.Empty)).Append("</td>");
                }

                sb.Append("</tr>");
            }

            sb.Append("</tbody></table>");
        }

        if (totals.Count > 0)
        {
            sb.Append("<section class=\"totals\">");
            foreach (var t in totals)
            {
                sb.Append("<div").Append(t.Strong ? " class=\"strong\"" : string.Empty).Append("><span>")
                  .Append(E(t.Label)).Append("</span><b>").Append(E(t.Value)).Append("</b></div>");
            }

            sb.Append("</section>");
        }

        if (!string.IsNullOrWhiteSpace(footnote))
        {
            sb.Append("<p class=\"note\">").Append(E(footnote)).Append("</p>");
        }

        sb.Append("<footer>تهیه شده در ").Append(E(Stamp())).Append("</footer></main></body></html>");
        return sb.ToString();
    }

    /// <summary>«yyyy/MM/dd HH:mm» in Persian digits (Jalali date + local clock time).</summary>
    public static string Stamp() =>
        PersianNumberFormatter.ToPersianDigits($"{JalaliCalendar.Today} {DateTime.Now:HH:mm}");

    private static string E(string? s) => WebUtility.HtmlEncode(s ?? string.Empty);

    private const string Css =
        "@page{size:A4;margin:12mm}*{box-sizing:border-box}" +
        "body{margin:0;font-family:'Vazirmatn',Tahoma,sans-serif;color:#101828;font-size:12px;background:#f2f4f7}" +
        ".bar{display:flex;justify-content:space-between;align-items:center;padding:10px 18px;background:#fff;border-bottom:1px solid #e4e7ec}" +
        ".bar button{font-family:inherit;padding:7px 16px;border:0;border-radius:8px;background:#4a6cf7;color:#fff;cursor:pointer}" +
        ".sheet{max-width:190mm;margin:14px auto;padding:14mm;background:#fff}" +
        "h1{margin:0;font-size:20px;font-weight:800}.sub{margin:4px 0 0;color:#667085}" +
        ".meta{display:grid;grid-template-columns:repeat(2,1fr);gap:6px 18px;margin:14px 0;padding:10px 12px;border:1px solid #e4e7ec;border-radius:8px}" +
        ".meta div{display:flex;justify-content:space-between;gap:10px}.meta span{color:#667085}" +
        "table{width:100%;border-collapse:collapse;margin-top:6px}" +
        "th,td{padding:6px 8px;border-bottom:1px solid #e4e7ec;text-align:right}" +
        "th{background:#f9fafb;font-weight:700;font-size:11px}tr{page-break-inside:avoid}thead{display:table-header-group}" +
        ".empty{text-align:center;color:#667085;padding:18px}" +
        ".totals{margin:14px 0 0 auto;width:60%}.totals div{display:flex;justify-content:space-between;padding:4px 0}" +
        ".totals .strong{border-top:2px solid #101828;margin-top:4px;padding-top:8px;font-size:14px}" +
        ".note{margin-top:14px;color:#667085}footer{margin-top:22px;padding-top:8px;border-top:1px dashed #d0d5dd;color:#98a2b3;font-size:10px}" +
        "@media print{body{background:#fff}.no-print{display:none}.sheet{margin:0;padding:0;max-width:none}}";
}

/// <summary>Shared «open the print preview, tell the user the outcome» step used by every screen's print command (X13).</summary>
public static class PrintServiceExtensions
{
    /// <summary>
    /// Opens <paramref name="html"/> in the browser preview. A failure (no browser association / sandboxed FS) becomes a
    /// danger toast; success a short info toast — the button never claims an action that did not happen.
    /// </summary>
    public static async Task<bool> OpenWithToastAsync(
        this IPrintService print, IToastService toast, string fileStem, string html)
    {
        var opened = await print.OpenHtmlAsync(fileStem, html);
        toast.Show(
            opened ? "پیش‌نمایش چاپ در مرورگر باز شد" : "باز کردن پیش‌نمایش چاپ ممکن نشد؛ مرورگر پیش‌فرض را بررسی کنید",
            opened ? Animora.Desktop.Controls.Tone.Info : Animora.Desktop.Controls.Tone.Danger);
        return opened;
    }
}
