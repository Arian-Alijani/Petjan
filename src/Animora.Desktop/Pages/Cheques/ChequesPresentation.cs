using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Animora.Contracts.Cheques;
using Animora.Desktop.Controls;
using Animora.Desktop.Localization;
using Avalonia;
using Avalonia.Media;

namespace Animora.Desktop.Pages.Cheques;

/// <summary>
/// Presentation adapters for the Cheques screen: the money/short-money and
/// Persian-number formatters, the status/direction tone+icon maps, the bank
/// gradient marks, the avatar palette and the countdown-tone derivation — all
/// transcribed from the reference (Animora-UI/assets/cheques-data.js
/// <c>money</c>/<c>moneyShort</c>/<c>numToWords</c>/<c>CH_STATUS</c>/<c>CH_DIR</c>
/// and the <c>.st.*</c>/<c>.dir-tag.*</c>/<c>.cd.*</c> rules in cheques.css).
/// Kept here so the ViewModel and View stay free of hardcoded literals.
/// </summary>
public static class ChequesPresentation
{
    // ── Number / money formatting ────────────────────────────────────────

    /// <summary>Persian digits for a plain integer (the reference's <c>fa</c>).</summary>
    public static string Fa(long value) =>
        PersianNumberFormatter.ToPersianDigits(value.ToString(CultureInfo.InvariantCulture));

    /// <summary>
    /// Persian digits for a text value, preserving non-digit characters — the
    /// reference's <c>fa(c.due)</c> shape (e.g. "1405/05/12" → «۱۴۰۵/۰۵/۱۲»).
    /// </summary>
    public static string FaText(string? text) => PersianNumberFormatter.ToPersianDigits(text ?? string.Empty);

    /// <summary>A solid fill brush from a hex string.</summary>
    public static IBrush Fill(string hex) => new SolidColorBrush(Color.Parse(hex));

    /// <summary>
    /// The reference's <c>money(n)</c> — a Persian-grouped integer
    /// (e.g. 8500000 → «۸٬۵۰۰٬۰۰۰»).
    /// </summary>
    public static string Money(long value) => PersianNumberFormatter.FormatGrouped(value);

    /// <summary>
    /// The reference's <c>moneyShort</c>: «۱۲٫۵ م» / «۱٫۲ میلیارد» /
    /// «۸۵۰ هزار» / plain Persian integer below 1000.
    /// </summary>
    public static string MoneyShort(long n)
    {
        if (n >= 1_000_000_000)
        {
            return Fa2(n, 1_000_000_000d) + " میلیارد";
        }

        if (n >= 1_000_000)
        {
            return Fa2(n, 1_000_000d) + " م";
        }

        if (n >= 1_000)
        {
            return Fa((long)Math.Round(n / 1_000d, MidpointRounding.AwayFromZero)) + " هزار";
        }

        return Fa(n);

        static string Fa2(long value, double unit)
        {
            var scaled = value / unit;
            var frac = value % (long)unit;
            var text = scaled.ToString(frac == 0 ? "0" : "0.#", CultureInfo.InvariantCulture);
            // The reference uses a Persian decimal separator (٫) via toLocaleString; we
            // render the ASCII dot then convert digits, matching its short shape.
            return PersianNumberFormatter.ToPersianDigits(text);
        }
    }

    /// <summary>
    /// The reference's <c>numToWords</c> — a Persian words rendering of an
    /// integer amount (e.g. 8500000 → «هشت میلیون و پانصد هزار»).
    /// </summary>
    public static string NumberToWords(long num)
    {
        num = Math.Abs(num);
        if (num == 0)
        {
            return "صفر";
        }

        string[] yekan = ["", "یک", "دو", "سه", "چهار", "پنج", "شش", "هفت", "هشت", "نه"];
        string[] dahgan = ["", "", "بیست", "سی", "چهل", "پنجاه", "شصت", "هفتاد", "هشتاد", "نود"];
        string[] dahyek = ["ده", "یازده", "دوازده", "سیزده", "چهارده", "پانزده", "شانزده", "هفده", "هجده", "نوزده"];
        string[] sadgan = ["", "صد", "دویست", "سیصد", "چهارصد", "پانصد", "ششصد", "هفتصد", "هشتصد", "نهصد"];
        string[] scale = ["", " هزار", " میلیون", " میلیارد", " هزار میلیارد"];

        var parts = new List<string>();
        var i = 0;
        while (num > 0)
        {
            var c = (int)(num % 1000);
            if (c != 0)
            {
                parts.Insert(0, Three(c) + scale[i]);
            }

            num /= 1000;
            i++;
        }

        return string.Join(" و ", parts);

        string Three(int n)
        {
            var outParts = new List<string>();
            var s = n / 100;
            var r = n % 100;
            if (s != 0)
            {
                outParts.Add(sadgan[s]);
            }

            if (r is >= 10 and <= 19)
            {
                outParts.Add(dahyek[r - 10]);
            }
            else
            {
                var d = r / 10;
                var y = r % 10;
                if (d != 0)
                {
                    outParts.Add(dahgan[d]);
                }

                if (y != 0)
                {
                    outParts.Add(yekan[y]);
                }
            }

            return string.Join(" و ", outParts);
        }
    }

    // ── Status / direction maps ──────────────────────────────────────────

    /// <summary>The reference's <c>CH_STATUS[k].label</c>.</summary>
    public static string StatusLabel(ChequeStatus status) => status switch
    {
        ChequeStatus.InHand => "در جریان",
        ChequeStatus.AtBank => "واگذار به بانک",
        ChequeStatus.Cleared => "وصول شده",
        ChequeStatus.Bounced => "برگشتی",
        ChequeStatus.Spent => "خرج شده",
        _ => "باطل شده",
    };

    /// <summary>The reference's <c>CH_STATUS[k].desc</c>.</summary>
    public static string StatusDesc(ChequeStatus status) => status switch
    {
        ChequeStatus.InHand => "نزد ما / صادر شده",
        ChequeStatus.AtBank => "در جریان وصول",
        ChequeStatus.Cleared => "پاس شده",
        ChequeStatus.Bounced => "عدم پرداخت",
        ChequeStatus.Spent => "واگذار به شخص ثالث",
        _ => "ابطال",
    };

    /// <summary>The reference's <c>TONE_C</c> status accent color.</summary>
    public static string StatusColor(ChequeStatus status) => status switch
    {
        ChequeStatus.InHand => "#1570cd",
        ChequeStatus.AtBank => "#b54708",
        ChequeStatus.Cleared => "#027a48",
        ChequeStatus.Bounced => "#d92d20",
        ChequeStatus.Spent => "#7b5cc4",
        _ => "#7a8b89",
    };

    /// <summary>The status-pill background/foreground, mirroring <c>.st.(ok|info|warn|danger|lilac|mute)</c>.</summary>
    public static (IBrush Background, IBrush Foreground) StatusPill(ChequeStatus status) => status switch
    {
        ChequeStatus.Cleared => (Fill("#ecfdf3"), Fill("#027a48")),
        ChequeStatus.InHand => (Fill("#eff8ff"), Fill("#1570cd")),
        ChequeStatus.AtBank => (Fill("#fffaeb"), Fill("#b54708")),
        ChequeStatus.Bounced => (Fill("#fef3f2"), Fill("#d92d20")),
        ChequeStatus.Spent => (Fill("#f4f3ff"), Fill("#7b5cc4")),
        _ => (Fill("#eef2f0"), Fill("#6b7d7a")),
    };

    /// <summary>The status icon key, mirroring <c>CH_STATUS[k].icon</c>.</summary>
    public static string StatusIcon(ChequeStatus status) => status switch
    {
        ChequeStatus.InHand => "IconWallet",
        ChequeStatus.AtBank => "IconBank",
        ChequeStatus.Cleared => "IconCheck",
        ChequeStatus.Bounced => "IconAlert",
        ChequeStatus.Spent => "IconShare",
        _ => "IconX",
    };

    /// <summary>The reference's <c>CH_DIR[k].label</c>.</summary>
    public static string DirectionLabel(ChequeDirection dir) => dir == ChequeDirection.In ? "دریافتی" : "پرداختی";

    /// <summary>The reference's <c>CH_DIR[k].short</c>.</summary>
    public static string DirectionShort(ChequeDirection dir) => dir == ChequeDirection.In ? "در" : "پر";

    /// <summary>The reference's <c>CH_DIR[k].icon</c>.</summary>
    public static string DirectionIcon(ChequeDirection dir) =>
        dir == ChequeDirection.In ? "IconArrowDownLeft" : "IconArrowUpRight";

    /// <summary>The direction-tag background/foreground, mirroring <c>.dir-tag.in/.out</c>.</summary>
    public static (IBrush Background, IBrush Foreground) DirectionTag(ChequeDirection dir) =>
        dir == ChequeDirection.In
            ? (Fill("#ecfdf3"), Fill("#027a48"))
            : (Fill("#fdf1e7"), Fill("#c06a29"));

    /// <summary>The amount text color, mirroring <c>.ch-amt.in</c> vs <c>.ch-amt.out</c>.</summary>
    public static IBrush AmountForeground(ChequeDirection dir) =>
        dir == ChequeDirection.In ? Fill("#027a48") : Fill("#c07227");

    /// <summary>The KPI accent color (reference's inline <c>--k</c>).</summary>
    public static string KpiColor(int index) => index switch
    {
        0 => "#027a48",
        1 => "#d98032",
        2 => "#b54708",
        _ => "#d92d20",
    };

    /// <summary>The KPI icon-tile fill, mirroring <c>.ch-kpi-ic</c> (<c>color-mix(--k 11%, #fff)</c>).</summary>
    public static IBrush KpiTileBackground(int index) => Fill(Mix(KpiColor(index), "#ffffff", 0.11));

    /// <summary>The KPI icon glyph color (the accent itself).</summary>
    public static IBrush KpiForeground(int index) => Fill(KpiColor(index));

    // ── Bank marks + avatars ─────────────────────────────────────────────

    /// <summary>The bank mark's 135° gradient (the reference's inline bank colours).</summary>
    public static IBrush BankGradient(string colorFrom, string colorTo) => new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(Color.Parse(colorFrom), 0),
            new GradientStop(Color.Parse(colorTo), 1),
        },
    };

    /// <summary>The bank distribution track fill (<c>linear-gradient(90deg, c2, c)</c>).</summary>
    public static IBrush BankTrackGradient(string colorFrom, string colorTo) => new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(Color.Parse(colorTo), 0),
            new GradientStop(Color.Parse(colorFrom), 1),
        },
    };

    private static readonly string[] AvatarColors =
    [
        "#8f74d1", "#e08a5c", "#4aa591", "#5c8ad6", "#d16060", "#eab749", "#b06fc4", "#027a48",
    ];

    /// <summary>The reference's <c>avColor</c> — a deterministic avatar colour from a name.</summary>
    public static string AvatarColor(string name)
    {
        var sum = name.Aggregate(0, (acc, ch) => acc + ch);
        return AvatarColors[sum % AvatarColors.Length];
    }

    /// <summary>The reference's <c>initials</c> — the first letter of the first two words.</summary>
    public static string Initials(string name)
    {
        var words = name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Concat(words.Take(2).Select(w => w[0]));
    }

    /// <summary>The avatar's 135° gradient (two derived colours), mirroring the reference inline style.</summary>
    public static IBrush AvatarGradient(string name) =>
        BankGradient(AvatarColor(name), AvatarColor(name + "x"));

    // ── Countdown pills ──────────────────────────────────────────────────

    /// <summary>The countdown pill class (<c>far|soon|now|past|done</c>), mirroring <c>cdInfo</c>.</summary>
    public static string CountdownClass(ChequeStatus status, int daysLeft) => status switch
    {
        ChequeStatus.Cleared or ChequeStatus.Void => "done",
        ChequeStatus.Bounced => "past",
        ChequeStatus.Spent => "done",
        _ => daysLeft < 0 ? "past"
            : daysLeft == 0 ? "now"
            : daysLeft <= 7 ? "soon"
            : "far",
    };

    /// <summary>The countdown pill text, mirroring <c>cdInfo(c).txt</c>.</summary>
    public static string CountdownText(ChequeStatus status, int daysLeft) => status switch
    {
        ChequeStatus.Cleared => "تسویه شد",
        ChequeStatus.Void => "باطل",
        ChequeStatus.Bounced => "برگشت خورده",
        ChequeStatus.Spent => "واگذار شد",
        _ => daysLeft < 0 ? $"{Fa(Math.Abs(daysLeft))} روز گذشته"
            : daysLeft == 0 ? "سررسید امروز"
            : $"{Fa(daysLeft)} روز مانده",
    };

    /// <summary>The countdown pill icon key, mirroring <c>cdInfo(c).icon</c>.</summary>
    public static string CountdownIcon(ChequeStatus status, int daysLeft) => status switch
    {
        ChequeStatus.Cleared => "IconCheck",
        ChequeStatus.Void => "IconCheck",
        ChequeStatus.Bounced => "IconAlert",
        ChequeStatus.Spent => "IconShare",
        _ => daysLeft < 0 ? "IconAlert"
            : daysLeft <= 7 ? "IconClock"
            : "IconCalendar",
    };

    /// <summary>The countdown pill background/foreground, mirroring <c>.cd.(far|soon|now|past|done)</c>.</summary>
    public static (IBrush Background, IBrush Foreground) CountdownPill(string cls) => cls switch
    {
        "soon" => (Fill("#fffaeb"), Fill("#b54708")),
        "now" => (Fill("#fef3f2"), Fill("#d92d20")),
        "past" => (Fill("#fef3f2"), Fill("#d92d20")),
        "done" => (Fill("#ecfdf3"), Fill("#027a48")),
        _ => (Fill("#eef2f0"), Fill("#536b68")),
    };

    // ── Chips ────────────────────────────────────────────────────────────

    /// <summary>The status chip's count-pill background tint, mirroring <c>.pb-chip.ch-chip.on .cnt</c>.</summary>
    public static string Mix(string hexA, string hexB, double weightA)
    {
        var a = Color.Parse(hexA);
        var b = Color.Parse(hexB);
        byte Mix(byte x, byte y) => (byte)Math.Round((x * weightA) + (y * (1 - weightA)));
        return $"#{Mix(a.R, b.R):X2}{Mix(a.G, b.G):X2}{Mix(a.B, b.B):X2}";
    }

    /// <summary>Maps a Phase 02 <see cref="Tone"/> for the side-card alert icons.</summary>
    public static Tone AlertTone(ChequeAlertKind kind) => kind switch
    {
        ChequeAlertKind.Today => Tone.Danger,
        ChequeAlertKind.Overdue => Tone.Danger,
        ChequeAlertKind.SoonOut => Tone.Warn,
        ChequeAlertKind.Bounced => Tone.Danger,
        _ => Tone.Ok,
    };

    /// <summary>The alert tile icon key, mirroring <c>cardAlerts</c>'s icons.</summary>
    public static string AlertIcon(ChequeAlertKind kind) => kind switch
    {
        ChequeAlertKind.Today => "IconClock",
        ChequeAlertKind.Overdue => "IconAlert",
        ChequeAlertKind.SoonOut => "IconWallet",
        ChequeAlertKind.Bounced => "IconShield",
        _ => "IconSms",
    };
}
