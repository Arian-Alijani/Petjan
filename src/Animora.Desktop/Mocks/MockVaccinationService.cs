using Animora.Contracts.Vaccination;

namespace Animora.Desktop.Mocks;

/// <summary>
/// Deterministic in-memory implementation of <see cref="IVaccinationService"/>.
/// The records, vaccine catalog and SMS templates are re-derived from what the
/// reference screen visually needs (Animora-UI/assets/vaccination-data.js) so
/// the rebuilt table, catalog cards, templates and compose preview render the
/// same boxes, counts and Persian text. Record dates are expressed relative to
/// the live Jalali "today" (the prototype pins 1404/05/11) so the derived
/// status distribution — and therefore every segment count, due/overdue label
/// and status pill — matches the reference exactly. This is the single swap
/// point a future backend replaces (R-18) — no ViewModel or View change is
/// required to do so.
/// </summary>
public sealed class MockVaccinationService : IVaccinationService
{
    // The prototype's pinned schedule is re-anchored to the live date so the
    // relative due/overdue labels keep their reference values.
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Today);

    private readonly List<VaccinationRecord> _records;
    private readonly IReadOnlyList<VaccineCatalogEntry> _catalog;
    private readonly IReadOnlyList<SmsTemplate> _templates;

    public MockVaccinationService()
    {
        _catalog =
        [
            new("FVRCP", "واکسن سه‌گانه گربه", "FVRCP", "cat", "a",
                "رینوتراکئیت، کالسی‌ویروس، پان‌لوکوپنی", 12, 24, 10),
            new("FeLV", "لوسمی گربه", "FeLV", "cat", "b",
                "ویروس لوسمی گربه‌سانان", 12, 8, 8),
            new("DHPP", "واکسن چهارگانه سگ", "DHPP", "dog", "c",
                "دیستمپر، هپاتیت، پاروو، پاراآنفلوانزا", 12, 31, 12),
            new("DHPPL", "واکسن هشت‌گانه سگ", "DHPPL", "dog", "e",
                "چهارگانه + لپتوسپیروز", 12, 5, 10),
            new("RABIES", "واکسن هاری", "Rabies", "all", "d",
                "ویروس هاری (زئونوز)", 12, 42, 15),
            new("KC", "سرفه کِنِل (کِنل‌کاف)", "KC", "dog", "f",
                "بوردتلا، پاراآنفلوانزا", 12, 3, 8),
            new("POX", "واکسن آبله پرندگان", "Pox", "bird", "b",
                "ویروس آبله طیور", 12, 14, 6),
            new("RHDV", "واکسن خرگوش (RHDV)", "RHDV", "rabbit", "a",
                "بیماری هموراژیک خرگوش", 12, 9, 6),
        ];

        _templates =
        [
            new("t1", "یادآوری نوبت واکسن", "reminder",
                "سلام {owner} عزیز، نوبت واکسن «{vaccine}» {pet} در تاریخ {date} فرا رسیده است. لطفاً جهت هماهنگی با کلینیک وت‌پلاس تماس بگیرید. ☎ ۰۲۱-۸۸۰۰۱۲۳۴"),
            new("t2", "اخطار سررسید گذشته", "overdue",
                "{owner} گرامی، واکسن «{vaccine}» {pet} از تاریخ {date} به تعویق افتاده است. برای حفظ سلامت حیوان لطفاً هرچه زودتر مراجعه فرمایید. کلینیک وت‌پلاس"),
            new("t3", "تأیید انجام واکسن", "done",
                "واکسن «{vaccine}» برای {pet} با موفقیت تزریق شد. نوبت بعدی: {next}. با آرزوی سلامتی — وت‌پلاس"),
            new("t4", "یادآوری سه روز مانده", "reminder",
                "یادآوری دوستانه 🐾 تنها ۳ روز تا نوبت واکسن {pet} ({vaccine}) در {date} باقی مانده است. وت‌پلاس"),
        ];

        _records = BuildRecords();
    }

    public Task<IReadOnlyList<VaccinationRecord>> GetRecordsAsync() =>
        Task.FromResult<IReadOnlyList<VaccinationRecord>>(_records.ToList());

    public Task<IReadOnlyList<VaccineCatalogEntry>> GetCatalogAsync() =>
        Task.FromResult(_catalog);

    public Task<IReadOnlyList<SmsTemplate>> GetSmsTemplatesAsync() =>
        Task.FromResult(_templates);

    public Task<VaccinationRecord> MarkDoneAsync(string recordId)
    {
        var index = _records.FindIndex(r => r.Id == recordId);
        if (index < 0)
        {
            throw new KeyNotFoundException($"Vaccination record '{recordId}' was not found.");
        }

        var updated = _records[index] with { Status = VaccinationStatus.Done };
        _records[index] = updated;
        return Task.FromResult(updated);
    }

    public Task<SmsPreview> BuildSmsPreviewAsync(string recordId, string templateId)
    {
        var record = _records.Find(r => r.Id == recordId)
            ?? throw new KeyNotFoundException($"Vaccination record '{recordId}' was not found.");
        var template = _templates.FirstOrDefault(t => t.Id == templateId) ?? _templates[0];
        var vaccine = _catalog.FirstOrDefault(v => v.Id == record.VaccineId);

        var body = template.Body
            .Replace("{owner}", record.OwnerName)
            .Replace("{pet}", record.PetName)
            .Replace("{vaccine}", vaccine?.Name ?? "واکسن")
            .Replace("{date}", PersianLong(record.DueDate))
            .Replace("{next}", PersianLong(record.DueDate));

        var chars = body.Length;
        var parts = chars == 0 ? 1 : (int)Math.Ceiling(chars / 70d);

        return Task.FromResult(new SmsPreview(record.Id, template.Id, body, chars, parts));
    }

    public Task<VaccinationRecord> SendSmsAsync(string recordId, string message)
    {
        var index = _records.FindIndex(r => r.Id == recordId);
        if (index < 0)
        {
            throw new KeyNotFoundException($"Vaccination record '{recordId}' was not found.");
        }

        // Network transmission is intentionally stubbed (phase file: no real
        // gateway). Only the reference's post-send state ("sent") is modeled.
        _ = message;
        var updated = _records[index] with { SmsState = SmsState.Sent };
        _records[index] = updated;
        return Task.FromResult(updated);
    }

    private static List<VaccinationRecord> BuildRecords()
    {
        // Deltas mirror the prototype's pinned 1404/05/11 "today"; smsState and
        // identity fields are transcribed from vaccination-data.js so the
        // rendered rows (vaccine tag, dose, owner, mobile, lot, site, vet) and
        // segment counts match 1:1.
        var rows = new (string Pet, string Species, string Owner, string Mobile, string Vaccine,
            string Dose, int? LastOffset, int DueOffset, string Vet, string Lot, string Site,
            string Tone, SmsState Sms, bool Done)[]
        {
            ("پشمک", "cat", "نگار محمدی", "0912 445 8890", "FVRCP", "نوبت سالانه", -370, 1, "دکتر آریا نیک", "FV-8821", "زیرجلدی شانه راست", "a", SmsState.Scheduled, false),
            ("لئو", "dog", "امیر رستگار", "0935 220 7781", "DHPP", "نوبت سالانه", -373, -2, "دکتر آریا نیک", "DH-3391", "زیرجلدی شانه چپ", "b", SmsState.Sent, false),
            ("آبی", "cat", "سارا یوسفی", "0901 118 2244", "RABIES", "نوبت اول", null, -5, "دکتر سمانه راد", null!, "زیرجلدی ران چپ", "c", SmsState.Sent, false),
            ("برفی", "dog", "علی نوروزی", "0919 663 1120", "DHPPL", "یادآور دوم", -370, -1, "دکتر آریا نیک", "DH-2214", "زیرجلدی شانه چپ", "d", SmsState.None, false),
            ("ماکسی", "dog", "رضا کاظمی", "0937 900 4412", "RABIES", "نوبت سالانه", -373, -10, "دکتر آریا نیک", "RB-6612", "زیرجلدی ران راست", "e", SmsState.Failed, false),
            ("نیمو", "cat", "مریم اکبری", "0912 771 3390", "FeLV", "نوبت اول", null, 7, "دکتر سمانه راد", null!, "زیرجلدی شانه راست", "f", SmsState.Scheduled, false),
            ("رکس", "dog", "حسین مرادی", "0938 442 7788", "KC", "نوبت سالانه", -373, 40, "دکتر آریا نیک", "KC-1180", "داخل بینی", "a", SmsState.None, false),
            ("کوکو", "bird", "زهرا سلطانی", "0930 512 6677", "POX", "نوبت سالانه", -370, 64, "دکتر سمانه راد", "PX-2210", "زیر بال", "b", SmsState.None, false),
            ("بلک", "cat", "محمد قاسمی", "0912 330 9911", "FVRCP", "یادآور اول", -370, 48, "دکتر آریا نیک", "FV-9014", "زیرجلدی شانه راست", "c", SmsState.None, false),
            ("هاپو", "dog", "فاطمه رحیمی", "0935 108 5566", "RABIES", "نوبت سالانه", -370, 365, "دکتر آریا نیک", "RB-7781", "زیرجلدی ران چپ", "d", SmsState.Sent, true),
            ("سیمبا", "cat", "کیانا فتحی", "0921 664 2210", "FVRCP", "نوبت اول", -370, 365, "دکتر سمانه راد", "FV-9120", "زیرجلدی شانه راست", "e", SmsState.Sent, true),
            ("گلی", "rabbit", "سعید بهرامی", "0919 220 8834", "RHDV", "نوبت سالانه", -370, -8, "دکتر آریا نیک", "RH-4412", "زیرجلدی گردن", "f", SmsState.None, false),
            ("تامی", "dog", "الهام نادری", "0937 771 0098", "DHPP", "یادآور سوم", -373, 3, "دکتر آریا نیک", "DH-6621", "زیرجلدی شانه چپ", "a", SmsState.None, false),
            ("میلو", "cat", "پویا اسدی", "0912 889 4471", "RABIES", "نوبت اول", -370, 365, "دکتر سمانه راد", "RB-8890", "زیرجلدی ران چپ", "b", SmsState.Sent, true),
            ("شارلوت", "dog", "نازنین شریفی", "0930 445 1120", "DHPPL", "نوبت سالانه", -373, 11, "دکتر آریا نیک", "DH-7734", "زیرجلدی شانه چپ", "c", SmsState.Scheduled, false),
            ("اسکار", "cat", "بهنام کریمی", "0938 220 6677", "FeLV", "یادآور اول", -370, 37, "دکتر سمانه راد", "FL-3320", "زیرجلدی شانه راست", "d", SmsState.None, false),
            ("جسی", "dog", "شیما موسوی", "0912 118 3344", "RABIES", "نوبت سالانه", -373, -11, "دکتر آریا نیک", "RB-2214", "زیرجلدی ران راست", "e", SmsState.Sent, false),
            ("فندق", "rodent", "آرش جعفری", "0935 662 8890", "RABIES", "نوبت اول", null, 9, "دکتر سمانه راد", null!, "زیرجلدی گردن", "f", SmsState.None, false),
            ("ونوس", "cat", "ترانه علوی", "0919 337 1122", "FVRCP", "یادآور دوم", -373, 0, "دکتر آریا نیک", "FV-5541", "زیرجلدی شانه راست", "a", SmsState.Scheduled, false),
            ("بادی", "dog", "وحید صادقی", "0937 220 4455", "KC", "نوبت سالانه", -370, 89, "دکتر آریا نیک", "KC-2290", "داخل بینی", "b", SmsState.None, false),
            ("پرنس", "dog", "یاسمن رفیعی", "0912 554 7788", "DHPP", "نوبت سالانه", -373, -9, "دکتر آریا نیک", "DH-8812", "زیرجلدی شانه چپ", "c", SmsState.None, false),
            ("لیلی", "cat", "سینا محمودی", "0930 118 9900", "RABIES", "نوبت سالانه", -370, 365, "دکتر سمانه راد", "RB-9931", "زیرجلدی ران چپ", "d", SmsState.Sent, true),
            ("رامبو", "dog", "مهسا کاشانی", "0938 771 2233", "DHPPL", "یادآور اول", -370, 25, "دکتر آریا نیک", "DH-5590", "زیرجلدی شانه چپ", "e", SmsState.None, false),
            ("چی‌چی", "bird", "کامران احمدی", "0912 663 8844", "POX", "نوبت اول", null, 2, "دکتر سمانه راد", null!, "زیر بال", "f", SmsState.None, false),
        };

        var list = new List<VaccinationRecord>(rows.Length);
        var uid = 1;
        foreach (var r in rows)
        {
            var due = Today.AddDays(r.DueOffset);
            var status = r.Done
                ? VaccinationStatus.Done
                : r.DueOffset < 0
                    ? VaccinationStatus.Overdue
                    : r.DueOffset <= 14
                        ? VaccinationStatus.Due
                        : VaccinationStatus.Scheduled;

            list.Add(new VaccinationRecord(
                Id: "vx-" + uid++,
                PetName: r.Pet,
                Species: r.Species,
                OwnerName: r.Owner,
                OwnerMobile: r.Mobile,
                VaccineId: r.Vaccine,
                Dose: r.Dose,
                LastDate: r.LastOffset is { } lo ? Today.AddDays(lo) : null,
                DueDate: due,
                VetName: r.Vet,
                Lot: r.Lot ?? "—",
                Site: r.Site,
                Tone: r.Tone,
                Status: status,
                SmsState: r.Sms));
        }

        return list;
    }

    // Mirrors the reference's VJ.long(): "۱۲ مرداد ۱۴۰۴".
    private static string PersianLong(DateOnly date)
    {
        var jalali = Localization.JalaliDate.FromDateTime(date.ToDateTime(TimeOnly.MinValue));
        return jalali.ToString("long");
    }
}
