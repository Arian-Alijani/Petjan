using System.Collections.Generic;
using System.Text;

namespace Animora.Desktop.Pages.Dashboard;

/// <summary>Pure text for the species donut's screen-reader/keyboard surface (no Avalonia types).</summary>
public static class SpeciesAccessibility
{
    /// <summary>"گربه: ۱۲ بیمار، ۴۰٪" — one legend entry as plain text, so the chart is never the only source of counts.</summary>
    public static string Entry(string name, string countText, string percentText) =>
        $"{name}: {countText} بیمار، {percentText}";

    /// <summary>Whole-donut summary; capped so a long species list stays short for a screen reader.</summary>
    public static string Summary(string totalText, IReadOnlyList<string> entries, int maxEntries = 6)
    {
        if (entries.Count == 0) return "نمودار گونه‌ها: داده‌ای وجود ندارد. برای باز کردن فهرست بیماران Enter بزنید.";
        var sb = new StringBuilder("نمودار گونه‌ها، مجموع ").Append(totalText).Append(" بیمار فعال. ");
        for (var i = 0; i < entries.Count && i < maxEntries; i++) sb.Append(entries[i]).Append("؛ ");
        if (entries.Count > maxEntries) sb.Append("و ").Append(entries.Count - maxEntries).Append(" گونه دیگر؛ ");
        return sb.Append("برای باز کردن فهرست بیماران Enter بزنید.").ToString();
    }
}
