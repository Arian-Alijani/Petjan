using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Animora.Contracts.Cheques;
using Animora.Desktop.Localization;

namespace Animora.Desktop.Mocks;

/// <summary>
/// In-memory <see cref="IChequesQueryService"/> implementation, seeded with the
/// reference's design-time cheque set (Animora-UI/assets/cheques-data.js:
/// <c>CH_BANKS</c> + <c>CHEQUES</c>). It is the single swappable seam a future
/// backend replaces (R-18); the cash-flow / bank / alert roll-ups are derived
/// here rather than stored, exactly as the reference computes them at render
/// time. Jalali day arithmetic goes through the Phase 04 helpers.
/// </summary>
public sealed class MockChequesQueryService : IChequesQueryService
{
    private static readonly ChequeBank[] BanksSource =
    [
        new("melli", "بانک ملی ایران", "ملی", "#12508f", "#2a79c4"),
        new("mellat", "بانک ملت", "ملت", "#b5123c", "#e0426b"),
        new("saderat", "بانک صادرات", "صادرات", "#173e8e", "#3a68c9"),
        new("tejarat", "بانک تجارت", "تجارت", "#146a9c", "#2f9bd4"),
        new("parsian", "بانک پارسیان", "پارسیان", "#7b1733", "#ad3556"),
        new("pasargad", "بانک پاسارگاد", "پاسارگاد", "#9c7418", "#d6a63a"),
        new("saman", "بانک سامان", "سامان", "#0d6ba8", "#2f9ada"),
        new("refah", "بانک رفاه", "رفاه", "#16704a", "#2fa073"),
        new("keshavarzi", "بانک کشاورزی", "کشاورزی", "#1c6b33", "#3d9a58"),
        new("ayandeh", "بانک آینده", "آینده", "#553a92", "#8163c4"),
        new("sepah", "بانک سپه", "سپه", "#1c5f8c", "#3f8dbd"),
        new("shahr", "بانک شهر", "شهر", "#a02b2b", "#cf5a52"),
    ];

    private static readonly Cheque[] ChequesSource =
    [
        new("CH-1051", ChequeDirection.In, "842517", "0184-2517-9930-4471", "mellat", "شعبه ونک", "مهسا رضایی", "مالک بیمار", "09121148820", 8500000, "1405/04/28", "1405/05/12", ChequeStatus.InHand, "تسویه فاکتور جراحی ارتوپدی «آنو»", "INV-14058217", "0203-8845-11", ""),
        new("CH-1049", ChequeDirection.In, "119640", "0311-9640-2287-6650", "melli", "شعبه شریعتی", "کامران دلاور", "مالک بیمار", "09355512207", 12400000, "1405/04/15", "1405/05/14", ChequeStatus.AtBank, "بستری و مراقبت ویژه «رکسی»", "INV-14058190", "0106-5521-04", "به شعبه مرکزی واگذار شد."),
        new("CH-1047", ChequeDirection.Out, "557102", "0455-7102-1178-9032", "pasargad", "شعبه میرداماد", "پخش دارویی سپهر", "تأمین‌کننده", "02188774411", 46800000, "1405/04/10", "1405/05/16", ChequeStatus.InHand, "فاکتور خرید دارو و مکمل — PO-3391", "PO-3391", "2101-9902-33", ""),
        new("CH-1045", ChequeDirection.In, "308874", "0230-8874-5561-2298", "saman", "شعبه جردن", "سمیرا کیانی", "مالک بیمار", "09127765430", 5250000, "1405/04/30", "1405/05/11", ChequeStatus.AtBank, "پکیج واکسیناسیون سالانه", "INV-14058244", "8330-1174-02", ""),
        new("CH-1044", ChequeDirection.In, "664219", "0466-4219-7745-1102", "parsian", "شعبه سعادت‌آباد", "آرش موحد", "مالک بیمار", "09122204471", 3200000, "1405/04/02", "1405/05/06", ChequeStatus.Bounced, "ویزیت و آزمایش خون", "INV-14058102", "0620-4471-19", "کسر موجودی — پیگیری تلفنی انجام شد."),
        new("CH-1042", ChequeDirection.Out, "901337", "0590-1337-8890-3312", "tejarat", "شعبه آرژانتین", "تجهیزات دامپزشکی رادین", "تأمین‌کننده", "02166553312", 92000000, "1405/03/24", "1405/05/25", ChequeStatus.InHand, "خرید دستگاه سونوگرافی پرتابل", "PO-3352", "2101-9902-33", "یک فقره از سه فقره چک قرارداد."),
        new("CH-1040", ChequeDirection.In, "473911", "0147-3911-2204-8876", "saderat", "شعبه گاندی", "نگین شفیعی", "مالک بیمار", "09193348821", 6750000, "1405/04/18", "1405/05/19", ChequeStatus.InHand, "عمل دندان و جرم‌گیری", "INV-14058173", "0106-5521-04", ""),
        new("CH-1038", ChequeDirection.In, "220458", "0122-0458-6612-7745", "keshavarzi", "شعبه پاسداران", "دامداری بهدشت", "مشتری سازمانی", "02177412200", 34500000, "1405/03/12", "1405/05/02", ChequeStatus.Cleared, "قرارداد پایش سلامت گله — تیر", "CNT-118", "0106-5521-04", ""),
        new("CH-1036", ChequeDirection.Out, "778120", "0677-8120-3345-9901", "refah", "شعبه ولیعصر", "اجاره مطب — آقای صدری", "موجر", "09121002233", 28000000, "1405/04/01", "1405/05/01", ChequeStatus.Cleared, "اجاره ماهانه مرداد", "EXP-2205", "2101-9902-33", ""),
        new("CH-1034", ChequeDirection.In, "392066", "0339-2066-1178-4432", "ayandeh", "شعبه ولنجک", "پانیذ حیدری", "مالک بیمار", "09128873310", 4100000, "1405/04/22", "1405/05/22", ChequeStatus.InHand, "گروم و بستری کوتاه‌مدت", "INV-14058221", "8330-1174-02", ""),
        new("CH-1032", ChequeDirection.In, "510473", "0451-0473-9982-1120", "melli", "شعبه نیاوران", "رضا فراهانی", "مالک بیمار", "09121190045", 9800000, "1405/04/08", "1405/05/28", ChequeStatus.Spent, "جراحی چشم «میلو»", "INV-14058144", "0106-5521-04", "بابت تسویه با پخش سپهر خرج شد."),
        new("CH-1030", ChequeDirection.Out, "846591", "0784-6591-2230-7761", "mellat", "شعبه ونک", "آزمایشگاه پاتوبیولوژی مهر", "تأمین‌کننده", "02188221177", 15600000, "1405/04/05", "1405/05/13", ChequeStatus.AtBank, "تسویه آزمایش‌های ارجاعی تیر", "PO-3378", "2101-9902-33", ""),
        new("CH-1028", ChequeDirection.In, "163925", "0216-3925-4471-8830", "sepah", "شعبه تجریش", "مریم اسدی", "مالک بیمار", "09354471203", 2850000, "1405/04/26", "1405/05/09", ChequeStatus.Cleared, "ویزیت تخصصی و رادیولوژی", "INV-14058231", "8330-1174-02", ""),
        new("CH-1026", ChequeDirection.In, "729384", "0672-9384-1102-5563", "shahr", "شعبه فرمانیه", "پت‌شاپ آرمان", "مشتری سازمانی", "02122334455", 18300000, "1405/04/12", "1405/06/03", ChequeStatus.InHand, "فروش عمده غذای درمانی", "INV-14058158", "0106-5521-04", ""),
        new("CH-1024", ChequeDirection.Out, "384710", "0538-4710-6690-2214", "saman", "شعبه جردن", "شرکت پخش نوین‌دارو", "تأمین‌کننده", "02177889900", 23900000, "1405/03/30", "1405/05/18", ChequeStatus.InHand, "سفارش دوره‌ای واکسن", "PO-3360", "2101-9902-33", ""),
        new("CH-1022", ChequeDirection.In, "957301", "0795-7301-8823-4470", "tejarat", "شعبه ظفر", "حامد نوروزی", "مالک بیمار", "09122998811", 7400000, "1405/03/28", "1405/04/30", ChequeStatus.Bounced, "جراحی اورژانسی «بلک»", "INV-14058088", "0106-5521-04", "مغایرت امضا — چک عودت داده شد."),
        new("CH-1020", ChequeDirection.In, "112907", "0111-2907-5540-6612", "pasargad", "شعبه الهیه", "شیوا مقدم", "مالک بیمار", "09121457788", 11200000, "1405/04/20", "1405/05/15", ChequeStatus.InHand, "درمان طولانی‌مدت نارسایی کلیه", "INV-14058205", "8330-1174-02", ""),
        new("CH-1018", ChequeDirection.Out, "640281", "0464-0281-1179-3345", "melli", "شعبه شریعتی", "خدمات نظافت پاک‌آور", "پیمانکار", "02144556677", 6400000, "1405/04/03", "1405/05/08", ChequeStatus.Cleared, "قرارداد نظافت فصلی", "EXP-2198", "2101-9902-33", ""),
        new("CH-1016", ChequeDirection.In, "285014", "0328-5014-7761-2290", "refah", "شعبه قیطریه", "الهام ساداتی", "مالک بیمار", "09127741122", 5900000, "1405/04/24", "1405/05/20", ChequeStatus.AtBank, "بستری سه‌روزه و سرم‌درمانی", "INV-14058226", "0106-5521-04", ""),
        new("CH-1014", ChequeDirection.In, "701163", "0570-1163-2284-9910", "saderat", "شعبه میرداماد", "باشگاه سگ‌های کاری آرتا", "مشتری سازمانی", "02126445566", 41000000, "1405/03/18", "1405/06/12", ChequeStatus.InHand, "قرارداد سالانه سلامت — قسط دوم", "CNT-124", "0106-5521-04", ""),
        new("CH-1012", ChequeDirection.Out, "459738", "0345-9738-6613-1102", "parsian", "شعبه سعادت‌آباد", "لیزینگ تجهیزات آریا", "تأمین‌کننده", "02188990011", 19750000, "1405/03/20", "1405/05/24", ChequeStatus.InHand, "قسط ماهانه دستگاه بیهوشی", "PO-3301", "2101-9902-33", ""),
        new("CH-1010", ChequeDirection.In, "836420", "0683-6420-9930-7745", "ayandeh", "شعبه زعفرانیه", "بهنام کریمی", "مالک بیمار", "09123310099", 3600000, "1405/04/06", "1405/04/28", ChequeStatus.Cleared, "واکسن هاری و میکروچیپ", "INV-14058121", "8330-1174-02", ""),
        new("CH-1008", ChequeDirection.In, "572901", "0257-2901-4478-6630", "mellat", "شعبه گیشا", "ندا پورعلی", "مالک بیمار", "09195540021", 8900000, "1405/04/29", "1405/05/26", ChequeStatus.InHand, "شیمی‌درمانی دوره‌ای «سیمبا»", "INV-14058248", "0106-5521-04", ""),
        new("CH-1006", ChequeDirection.Out, "318665", "0431-8665-2201-9934", "sepah", "شعبه میدان کاج", "چاپ و تبلیغات رسانه‌نو", "پیمانکار", "02133445566", 4300000, "1405/04/14", "1405/05/21", ChequeStatus.InHand, "کمپین تبلیغاتی پاییز", "EXP-2211", "2101-9902-33", ""),
        new("CH-1004", ChequeDirection.In, "904472", "0790-4472-1163-5520", "keshavarzi", "شعبه شهرک غرب", "فرهاد بیات", "مالک بیمار", "09121876655", 2400000, "1405/04/11", "1405/05/03", ChequeStatus.Void, "ویزیت — لغو شد", "INV-14058151", "0106-5521-04", "به درخواست مالک ابطال و نقدی پرداخت شد."),
        new("CH-1002", ChequeDirection.In, "661158", "0166-1158-3392-4470", "saman", "شعبه ونک", "کلینیک همکار پارس", "مشتری سازمانی", "02188006611", 27500000, "1405/04/16", "1405/06/20", ChequeStatus.InHand, "ارجاع بیماران جراحی — تسویه فصلی", "CNT-131", "0106-5521-04", ""),
        new("CH-1000", ChequeDirection.Out, "147203", "0214-7203-8830-1176", "shahr", "شعبه اندرزگو", "بیمه تکمیلی دام‌یار", "تأمین‌کننده", "02191001100", 12800000, "1405/04/09", "1405/05/10", ChequeStatus.AtBank, "حق بیمه فصلی پرسنل", "EXP-2203", "2101-9902-33", ""),
        new("CH-0998", ChequeDirection.In, "523864", "0352-3864-9917-2203", "melli", "شعبه پونک", "سعید ملکی", "مالک بیمار", "09364471290", 4950000, "1405/04/19", "1405/05/17", ChequeStatus.InHand, "ارتوپدی و فیزیوتراپی", "INV-14058186", "8330-1174-02", ""),
    ];

    private readonly List<Cheque> _cheques = [.. ChequesSource];

    private static ChequeBank ResolveBank(IEnumerable<ChequeBank> banks, string key) =>
        banks.FirstOrDefault(b => b.Key == key)
        ?? new ChequeBank(key, "—", "—", "#7a8b89", "#9aa8a5");

    /// <summary>Whether a status is still open (mirrors the reference's <c>isOpen</c>).</summary>
    private static bool IsOpen(Cheque c) => c.Status is ChequeStatus.InHand or ChequeStatus.AtBank;

    private static int DaysLeft(Cheque c, string today) =>
        JalaliCalendar.Diff(c.DueDate, today);

    public Task<IReadOnlyList<Cheque>> GetAllAsync() =>
        Task.FromResult<IReadOnlyList<Cheque>>(_cheques.ToList());

    public Task<IReadOnlyList<ChequeBank>> GetBanksAsync() =>
        Task.FromResult<IReadOnlyList<ChequeBank>>(BanksSource.ToList());

    public Task<CashflowSummary> GetCashflowAsync()
    {
        var today = JalaliCalendar.Today;
        var t0 = JalaliCalendar.ToDay(today);
        var weeks = new List<CashflowWeek>(8);
        for (var i = 0; i < 8; i++)
        {
            var a = t0 + (i * 7);
            var b = a + 6;
            long inSum = 0;
            long outSum = 0;
            foreach (var c in _cheques)
            {
                if (!IsOpen(c))
                {
                    continue;
                }

                var d = JalaliCalendar.ToDay(c.DueDate);
                if (d < a || d > b)
                {
                    continue;
                }

                if (c.Direction == ChequeDirection.In)
                {
                    inSum += c.Amount;
                }
                else
                {
                    outSum += c.Amount;
                }
            }

            weeks.Add(new CashflowWeek(JalaliCalendar.Medium(JalaliCalendar.FromDay(a)), inSum, outSum));
        }

        return Task.FromResult(new CashflowSummary(weeks));
    }

    public Task<IReadOnlyList<BankSummary>> GetBankBreakdownAsync()
    {
        var rows = _cheques
            .GroupBy(c => c.BankKey)
            .Select(g =>
            {
                var b = ResolveBank(BanksSource, g.Key);
                return new BankSummary(
                    g.Key, b.Name, b.Short, b.ColorFrom, b.ColorTo,
                    g.Sum(c => c.Amount), g.Count());
            })
            .OrderByDescending(r => r.Amount)
            .Take(5)
            .ToList();

        return Task.FromResult<IReadOnlyList<BankSummary>>(rows);
    }

    public Task<Cheque> CreateAsync(NewChequeRequest request)
    {
        // Mirrors saveCheque's insert path: id = "CH-" + (1100 + count), status
        // forced to inhand, and the demo fields filled with the same placeholders.
        var id = $"CH-{(1100 + _cheques.Count).ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        var cheque = new Cheque(
            id,
            request.Direction,
            request.Number,
            string.IsNullOrWhiteSpace(request.Sayad) ? "0000-0000-0000-0000" : request.Sayad,
            request.BankKey,
            string.IsNullOrWhiteSpace(request.Branch) ? "شعبه مرکزی" : request.Branch,
            request.Party,
            request.Role,
            string.IsNullOrWhiteSpace(request.Phone) ? "09120000000" : request.Phone,
            request.Amount,
            request.IssueDate,
            request.DueDate,
            ChequeStatus.InHand,
            string.IsNullOrWhiteSpace(request.Subject) ? "—" : request.Subject,
            string.IsNullOrWhiteSpace(request.Reference) ? "—" : request.Reference,
            request.Account,
            request.Note);

        _cheques.Insert(0, cheque);
        return Task.FromResult(cheque);
    }

    public Task UpdateAsync(string chequeId, UpdateChequeRequest request)
    {
        var index = _cheques.FindIndex(c => c.Id == chequeId);
        if (index >= 0)
        {
            var existing = _cheques[index];
            _cheques[index] = existing with
            {
                Direction = request.Direction,
                BankKey = request.BankKey,
                Number = request.Number,
                Sayad = request.Sayad,
                Party = request.Party,
                Role = request.Role,
                Phone = request.Phone,
                Amount = request.Amount,
                IssueDate = request.IssueDate,
                DueDate = request.DueDate,
                Branch = request.Branch,
                Subject = request.Subject,
                Reference = request.Reference,
                Note = request.Note,
                Account = request.Account,
            };
        }

        return Task.CompletedTask;
    }

    public Task DeleteAsync(string chequeId)
    {
        _cheques.RemoveAll(c => c.Id == chequeId);
        return Task.CompletedTask;
    }

    public Task BulkUpdateStatusAsync(IReadOnlyList<string> ids, ChequeStatus newStatus)
    {
        var set = new HashSet<string>(ids, StringComparer.Ordinal);
        for (var i = 0; i < _cheques.Count; i++)
        {
            if (set.Contains(_cheques[i].Id))
            {
                _cheques[i] = _cheques[i] with { Status = newStatus };
            }
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ChequeAlert>> GetAlertsAsync()
    {
        var today = JalaliCalendar.Today;
        var overdue = _cheques.Where(c => IsOpen(c) && DaysLeft(c, today) < 0).ToList();
        var dueToday = _cheques.Where(c => IsOpen(c) && DaysLeft(c, today) == 0).ToList();
        var soonOut = _cheques.Where(c =>
            c.Direction == ChequeDirection.Out && IsOpen(c) &&
            DaysLeft(c, today) > 0 && DaysLeft(c, today) <= 5).ToList();
        var bounced = _cheques.Where(c => c.Status == ChequeStatus.Bounced).ToList();

        var items = new List<ChequeAlert>();
        if (dueToday.Count > 0)
        {
            items.Add(new ChequeAlert(
                ChequeAlertKind.Today,
                $"{PersianNumberFormatter.ToPersianDigits(dueToday.Count)} چک امروز سررسید می‌شود",
                $"مجموع {ChequeMoneyFormat.Short(dueToday.Sum(c => c.Amount))} تومان"));
        }

        if (overdue.Count > 0)
        {
            items.Add(new ChequeAlert(
                ChequeAlertKind.Overdue,
                $"{PersianNumberFormatter.ToPersianDigits(overdue.Count)} چک سررسید گذشته دارد",
                "نیازمند پیگیری فوری"));
        }

        if (soonOut.Count > 0)
        {
            items.Add(new ChequeAlert(
                ChequeAlertKind.SoonOut,
                $"تأمین وجه {PersianNumberFormatter.ToPersianDigits(soonOut.Count)} چک پرداختی",
                $"تا ۵ روز آینده · {ChequeMoneyFormat.Short(soonOut.Sum(c => c.Amount))} تومان"));
        }

        if (bounced.Count > 0)
        {
            items.Add(new ChequeAlert(
                ChequeAlertKind.Bounced,
                $"{PersianNumberFormatter.ToPersianDigits(bounced.Count)} چک برگشتی باز",
                "ثبت پیگیری حقوقی توصیه می‌شود"));
        }

        items.Add(new ChequeAlert(
            ChequeAlertKind.Sms,
            "ارسال پیامک یادآوری سررسید",
            "برای مالکین چک‌های هفته آینده"));

        return Task.FromResult<IReadOnlyList<ChequeAlert>>(items);
    }
}

/// <summary>
/// A tiny money-short formatter shared by the mock's alert text (the reference's
/// <c>moneyShort</c>): «۱۲٫۵ م» / «۱٫۲ میلیارد» / «۸۵۰ هزار». Kept here so the
/// mock produces the exact reference strings without depending on the Desktop
/// presentation layer (which lives in the Desktop project).
/// </summary>
internal static class ChequeMoneyFormat
{
    public static string Short(long n)
    {
        static string Fa(string s) => PersianNumberFormatter.ToPersianDigits(s);

        if (n >= 1_000_000_000)
        {
            var v = n / 1_000_000_000d;
            return Fa(v.ToString(n % 1_000_000_000 == 0 ? "0" : "0.#",
                System.Globalization.CultureInfo.InvariantCulture)) + " میلیارد";
        }

        if (n >= 1_000_000)
        {
            var v = n / 1_000_000d;
            return Fa(v.ToString(n % 1_000_000 == 0 ? "0" : "0.#",
                System.Globalization.CultureInfo.InvariantCulture)) + " م";
        }

        if (n >= 1_000)
        {
            return Fa(Math.Round(n / 1_000d, MidpointRounding.AwayFromZero)
                .ToString(System.Globalization.CultureInfo.InvariantCulture)) + " هزار";
        }

        return Fa(n.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
}
