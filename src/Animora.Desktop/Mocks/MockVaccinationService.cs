using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Animora.Contracts.Identity;
using Animora.Contracts.Vaccination;
using Animora.Desktop.Localization;

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

    // The audit identity seam (R1/R4): every recorded mutation is stamped from it.
    private readonly ICurrentUserContext _user;

    // Vaccination-history ledger (R4). Built lazily into a cached row list and
    // rebuilt only after a mutation (PERFORMANCE.md §13).
    private readonly List<VaccinationHistoryEntry> _history = [];
    private IReadOnlyList<HistoryRow>? _historyRows;
    private bool _historyDirty = true;

    private static readonly ActorRef[] SeedActors =
    [
        new("u-aria", "دکتر آریا نیک", "مدیر کلینیک", "آ"),
        new("u-samaneh", "دکتر سمانه راد", "دکتر دامپزشک", "س"),
        new("u-sara", "منشی سارا", "پذیرش", "س"),
    ];

    // O(1) id -> list-index lookup (PERFORMANCE.md §2/§13): records are only
    // ever mutated in place (never inserted/removed), so this index never
    // needs a rebuild after construction.
    private readonly Dictionary<string, int> _recordIndexById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, VaccineCatalogEntry> _catalogById;
    private readonly Dictionary<string, SmsTemplate> _templatesById;

    public MockVaccinationService(ICurrentUserContext user)
    {
        _user = user;
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

        _catalogById = _catalog.ToDictionary(c => c.Id, StringComparer.Ordinal);
        _templatesById = _templates.ToDictionary(t => t.Id, StringComparer.Ordinal);
        RebuildRecordIndex();
        SeedHistory();
    }

    // ── History (R4) ────────────────────────────────────────────────────────

    private ActorRef CurrentActor() =>
        new(_user.UserId, _user.Name, _user.RoleLabel, _user.Initials);

    private static string Jalali(DateOnly date) =>
        JalaliCalendar.ToIso(JalaliDate.FromDateTime(date.ToDateTime(TimeOnly.MinValue)));

    private static string NowTime() =>
        PersianNumberFormatter.ToPersianDigits($"{DateTime.Now.Hour:D2}:{DateTime.Now.Minute:D2}");

    private static string KindLabel(VaccinationHistoryKind kind) => kind switch
    {
        VaccinationHistoryKind.Administered => "تزریق شد",
        VaccinationHistoryKind.Scheduled => "زمان‌بندی شد",
        VaccinationHistoryKind.SmsSent => "پیامک ارسال شد",
        VaccinationHistoryKind.Rescheduled => "باززمان‌بندی شد",
        _ => "رویداد",
    };

    /// <summary>Seeds the history backlog: done records → Administered rows dated on their LastDate.</summary>
    private void SeedHistory()
    {
        foreach (var record in _records)
        {
            var vaccine = _catalogById.GetValueOrDefault(record.VaccineId);
            var actor = ActorFor(record.VetName);

            if (record.Status == VaccinationStatus.Done && record.LastDate is { } last)
            {
                AddHistory(new VaccinationHistoryEntry(
                    $"vh-done-{record.Id}", VaccinationHistoryKind.Administered, KindLabel(VaccinationHistoryKind.Administered),
                    record.Id, record.PetName, record.Species, record.OwnerName,
                    vaccine?.Name ?? "واکسن", record.Dose, Jalali(last), "۱۰:۳۰",
                    $"تزریق {record.Dose} — نوبت بعدی {Jalali(record.DueDate)}", actor));
            }
            else
            {
                AddHistory(new VaccinationHistoryEntry(
                    $"vh-sched-{record.Id}", VaccinationHistoryKind.Scheduled, KindLabel(VaccinationHistoryKind.Scheduled),
                    record.Id, record.PetName, record.Species, record.OwnerName,
                    vaccine?.Name ?? "واکسن", record.Dose, Jalali(record.DueDate), "۰۹:۰۰",
                    "زمان‌بندی نوبت واکسن", actor));
            }

            if (record.SmsState == SmsState.Sent)
            {
                AddHistory(new VaccinationHistoryEntry(
                    $"vh-sms-{record.Id}", VaccinationHistoryKind.SmsSent, KindLabel(VaccinationHistoryKind.SmsSent),
                    record.Id, record.PetName, record.Species, record.OwnerName,
                    vaccine?.Name ?? "واکسن", record.Dose, Jalali(record.DueDate), "۰۸:۱۵",
                    "ارسال یادآور پیامکی به مالک", ActorFor("منشی سارا")));
            }
        }

        _historyDirty = true;
    }

    private static ActorRef ActorFor(string vetName) =>
        vetName.Contains("سمانه", StringComparison.Ordinal) ? SeedActors[1]
        : vetName.Contains("آریا", StringComparison.Ordinal) ? SeedActors[0]
        : SeedActors[2];

    private void AddHistory(VaccinationHistoryEntry entry)
    {
        _history.Add(entry);
        _historyDirty = true;
    }

    private IReadOnlyList<HistoryRow> EnsureHistory()
    {
        if (_historyDirty || _historyRows is null)
        {
            _historyRows = _history
                .Select(e => new HistoryRow(e, PersianText.Normalize(
                    $"{e.PetName} {e.OwnerName} {e.VaccineName} {e.Dose} {e.Note} {e.Actor.Name} {e.RecordId}")))
                .ToList();
            _historyDirty = false;
        }

        return _historyRows;
    }

    public Task<VaccinationHistoryPage> GetHistoryAsync(VaccinationHistoryFilter filter)
    {
        var rows = EnsureHistory();
        var kinds = filter.Kinds ?? [];

        var from = string.IsNullOrWhiteSpace(filter.FromDate) ? null : filter.FromDate;
        var to = string.IsNullOrWhiteSpace(filter.ToDate) ? null : filter.ToDate;
        var q = PersianText.Normalize(filter.Search);

        var matched = new List<HistoryRow>(rows.Count);
        foreach (var row in rows)
        {
            var e = row.Entry;
            if (from is not null && string.CompareOrdinal(e.Date, from) < 0)
            {
                continue;
            }

            if (to is not null && string.CompareOrdinal(e.Date, to) > 0)
            {
                continue;
            }

            if (kinds.Count > 0 && !kinds.Contains(e.Kind))
            {
                continue;
            }

            if (q.Length > 0 && !PersianText.Contains(row.Haystack, q))
            {
                continue;
            }

            matched.Add(row);
        }

        matched.Sort(filter.Sort == VaccinationHistorySort.Oldest ? OldestComparer : NewestComparer);

        var pageSize = Math.Max(1, filter.PageSize);
        var skip = (Math.Max(1, filter.Page) - 1) * pageSize;
        var take = Math.Min(pageSize, Math.Max(0, matched.Count - skip));

        IReadOnlyList<VaccinationHistoryEntry> items = take <= 0
            ? []
            : matched.GetRange(skip, take).Select(r => r.Entry).ToList();

        return Task.FromResult(new VaccinationHistoryPage(items, matched.Count));
    }

    private static int CompareDate(HistoryRow a, HistoryRow b, bool ascending)
    {
        var byDate = string.CompareOrdinal(a.Entry.Date, b.Entry.Date);
        if (byDate == 0)
        {
            byDate = string.CompareOrdinal(a.Entry.Time, b.Entry.Time);
        }

        if (byDate == 0)
        {
            byDate = string.CompareOrdinal(a.Entry.Id, b.Entry.Id);
        }

        return ascending ? byDate : -byDate;
    }

    private static readonly Comparison<HistoryRow> NewestComparer = static (a, b) => CompareDate(a, b, ascending: false);
    private static readonly Comparison<HistoryRow> OldestComparer = static (a, b) => CompareDate(a, b, ascending: true);

    private sealed record HistoryRow(VaccinationHistoryEntry Entry, string Haystack);

    private void RebuildRecordIndex()
    {
        _recordIndexById.Clear();
        for (var i = 0; i < _records.Count; i++)
        {
            _recordIndexById[_records[i].Id] = i;
        }
    }

    public Task<IReadOnlyList<VaccinationRecord>> GetRecordsAsync() =>
        Task.FromResult<IReadOnlyList<VaccinationRecord>>(_records.ToList());

    public Task<IReadOnlyList<VaccineCatalogEntry>> GetCatalogAsync() =>
        Task.FromResult(_catalog);

    public Task<IReadOnlyList<SmsTemplate>> GetSmsTemplatesAsync() =>
        Task.FromResult(_templates);

    public Task<VaccinationRecord> MarkDoneAsync(string recordId)
    {
        var index = _recordIndexById.GetValueOrDefault(recordId, -1);
        if (index < 0)
        {
            throw new KeyNotFoundException($"Vaccination record '{recordId}' was not found.");
        }

        var updated = _records[index] with { Status = VaccinationStatus.Done, LastDate = Today };
        _records[index] = updated;

        var vaccine = _catalogById.GetValueOrDefault(updated.VaccineId);
        AddHistory(new VaccinationHistoryEntry(
            $"vh-done-{updated.Id}-{_history.Count}", VaccinationHistoryKind.Administered,
            KindLabel(VaccinationHistoryKind.Administered), updated.Id, updated.PetName, updated.Species,
            updated.OwnerName, vaccine?.Name ?? "واکسن", updated.Dose,
            JalaliCalendar.Today, NowTime(), "ثبت انجام واکسن", CurrentActor()));

        return Task.FromResult(updated);
    }

    public Task<SmsPreview> BuildSmsPreviewAsync(string recordId, string templateId)
    {
        var record = _recordIndexById.TryGetValue(recordId, out var rIndex) ? _records[rIndex] : null;
        if (record is null)
        {
            throw new KeyNotFoundException($"Vaccination record '{recordId}' was not found.");
        }

        var template = _templatesById.GetValueOrDefault(templateId) ?? _templates[0];
        var vaccine = _catalogById.GetValueOrDefault(record.VaccineId);

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
        var index = _recordIndexById.GetValueOrDefault(recordId, -1);
        if (index < 0)
        {
            throw new KeyNotFoundException($"Vaccination record '{recordId}' was not found.");
        }

        // Network transmission is intentionally stubbed (phase file: no real
        // gateway). Only the reference's post-send state ("sent") is modeled.
        _ = message;
        var updated = _records[index] with { SmsState = SmsState.Sent };
        _records[index] = updated;

        var vaccine = _catalogById.GetValueOrDefault(updated.VaccineId);
        AddHistory(new VaccinationHistoryEntry(
            $"vh-sms-{updated.Id}-{_history.Count}", VaccinationHistoryKind.SmsSent,
            KindLabel(VaccinationHistoryKind.SmsSent), updated.Id, updated.PetName, updated.Species,
            updated.OwnerName, vaccine?.Name ?? "واکسن", updated.Dose,
            JalaliCalendar.Today, NowTime(), "ارسال یادآور پیامکی به مالک", CurrentActor()));

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
