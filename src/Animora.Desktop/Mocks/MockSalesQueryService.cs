using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Animora.Contracts.Sales;
using Animora.Desktop.Localization;

namespace Animora.Desktop.Mocks;

/// <summary>
/// Deterministic in-memory implementation of <see cref="ISalesQueryService"/>
/// — the single swappable seam a future backend replaces (R-18). Its sample
/// data is re-derived from what the reference (<c>Animora-UI/assets/
/// clinic-data.js</c> + <c>sale-page.js</c>) renders: a few completed «آمادهٔ
/// فاکتور» encounters and a spread of invoices across every state, carrying the
/// same line/total arithmetic as <c>FLOW.calcInvoice</c>. It is stateful: the
/// build/blank mutations append to the same list so navigation preserves them.
/// </summary>
public sealed class MockSalesQueryService : ISalesQueryService
{
    private readonly List<ReadyEncounter> _encounters;
    private readonly List<InvoiceRecord> _invoices = [];
    private int _invoiceCounter = 5;

    private static readonly InsurerRef[] Insurers =
    [
        new("organ-1", "بیمه آسیا — پوشش حیوانات خانگی", 15),
        new("organ-2", "بیمه پاسارگاد — پوشش درمان", 20),
        new("organ-3", "بیمه دانا — پوشش جامع", 25),
        new("organ-4", "بیمه ملت — پوشش پایه", 10),
    ];

    private static readonly string[] AvatarTones = ["a", "b", "c", "d", "e", "f"];

    public MockSalesQueryService()
    {
        _encounters =
        [
            new("enc-3", "qt-1", "نگار محمدی", "پشمک", "گربه پرشین", "آریا نیک", "دکتر آریا نیک",
                "11:05", [new("sv-2", InvoiceLineKind.Service, "واکسن چهارگانه برونشیت", 1, 380_000),
                          new("sv-1", InvoiceLineKind.Service, "ویزیت عمومی", 1, 250_000),
                          new("it-9", InvoiceLineKind.Item, "ویتامین مایع", 1, 150_000)]),
            new("enc-4", "qt-2", "امیر رستگار", "لئو", "سگ ژرمن", "مینا همتی", "دکتر مینا همتی",
                "10:30", [new("sv-3", InvoiceLineKind.Service, "جراحی عقیم‌سازی", 1, 2_800_000),
                          new("sv-6", InvoiceLineKind.Service, "آزمایش خون کامل", 1, 800_000),
                          new("it-6", InvoiceLineKind.Item, "آنتی‌بیوتیک خوراکی", 1, 320_000),
                          new("it-4", InvoiceLineKind.Item, "شامپوی مخصوص حیوانات", 1, 220_000)]),
            new("enc-5", "qt-3", "محسن دهقان", "رکس", "سگ هاسکی", "آریا نیک", "دکتر آریا نیک",
                "12:20", [new("sv-1", InvoiceLineKind.Service, "ویزیت عمومی", 1, 250_000),
                          new("sv-10", InvoiceLineKind.Service, "تزریق و سرم‌تراپی", 1, 450_000),
                          new("it-1", InvoiceLineKind.Item, "قرص ضد انگل", 2, 160_000)]),
            new("enc-6", "qt-4", "سارا یوسفی", "آبی", "کاسکو", "سهیل کاویانی", "دکتر سهیل کاویانی",
                "13:05", [new("sv-4", InvoiceLineKind.Service, "دندان‌پزشکی و جرم‌گیری", 1, 1_200_000)]),
        ];

        // INV-001 — draft built from a visit, with discount + insurance.
        var inv1 = NewRecord("INV-001", InvoiceKind.Visit, "enc-1", "مریم آذری", "کوکو", "۰۹۱۰۵۵۶۶۷۷۸",
            [new("sv-1", InvoiceLineKind.Service, "ویزیت عمومی", 1, 250_000),
             new("it-1", InvoiceLineKind.Item, "قرص ضد انگل", 2, 160_000)],
            DiscountKind.Percent, 10, "organ-2", InvoiceState.Draft, 0, []);

        // INV-002 — partial direct sale, one cash prepayment.
        var inv2 = NewRecord("INV-002", InvoiceKind.Sale, null, "سارا یوسفی", "آبی", "۰۹۱۹۸۷۶۵۴۳۲",
            [new("sv-9", InvoiceLineKind.Service, "ویزیت تخصصی پوست", 1, 350_000),
             new("it-5", InvoiceLineKind.Item, "مکمل تقویت مفصل", 1, 450_000)],
            DiscountKind.Percent, 0, null, InvoiceState.Partial, 400_000,
            [new(PaymentMethod.Cash, "پیش‌پرداخت نقدی", "11:20", 400_000)]);

        // INV-003 — settled visit invoice.
        var inv3 = NewRecord("INV-003", InvoiceKind.Visit, null, "رضا کریمی", "میلو", "۰۹۳۰۴۴۵۵۶۶۷",
            [new("sv-5", InvoiceLineKind.Service, "آرایش و پیرایش", 1, 650_000),
             new("it-2", InvoiceLineKind.Item, "غذای کنسرو مرغ", 2, 85_000)],
            DiscountKind.Percent, 0, null, InvoiceState.Settled, 921_000,
            [new(PaymentMethod.Card, "پرداخت کارتی", "12:40", 921_000)]);

        // INV-004 — voided walk-in invoice.
        var inv4 = NewRecord("INV-004", InvoiceKind.Sale, null, "الهام صادقی", "نانا", "۰۹۱۲۷۷۸۸۹۹۰",
            [new("it-8", InvoiceLineKind.Item, "خاک مخصوص گربه", 1, 280_000),
             new("it-10", InvoiceLineKind.Item, "قلاده ضدکک", 1, 390_000)],
            DiscountKind.Percent, 0, null, InvoiceState.Void, 0, []);

        // INV-005 — refunded settled invoice with a negative payment.
        var inv5 = NewRecord("INV-005", InvoiceKind.Sale, null, "مریم آذری", "کوکو", "۰۹۱۰۵۵۶۶۷۷۸",
            [new("it-3", InvoiceLineKind.Item, "غذای خشک بالغ سگ (۵ کیلو)", 1, 850_000)],
            DiscountKind.Percent, 0, null, InvoiceState.Refunded, 0,
            [new(PaymentMethod.Cash, "پرداخت نقدی", "13:10", 926_500),
             new(PaymentMethod.Cash, "عودت وجه", "13:35", -926_500)]);

        _invoices.AddRange([inv1, inv2, inv3, inv4, inv5]);
        RecalcAll();
    }

    public Task<IReadOnlyList<ReadyEncounterRef>> GetReadyEncountersAsync()
    {
        IReadOnlyList<ReadyEncounterRef> list = _encounters
            .Where(e => !_invoices.Any(i => i.EncounterId == e.Id))
            .Select(e =>
            {
                var tone = AvatarTones[Math.Abs(e.PetName.GetHashCode()) % AvatarTones.Length];
                return new ReadyEncounterRef(
                    e.Id, e.TicketId, e.OwnerName, e.PetName, e.PetKind,
                    e.DoctorShort, e.DoctorName, e.CompletedAt,
                    e.Lines.Count, e.Lines.Sum(l => l.UnitPrice * l.Quantity),
                    tone, FirstChar(e.OwnerName));
            })
            .ToList();

        return Task.FromResult(list);
    }

    public Task<IReadOnlyList<InvoiceSummary>> GetInvoicesAsync()
    {
        IReadOnlyList<InvoiceSummary> list = _invoices
            .Select(i => new InvoiceSummary(
                i.Id, i.Kind, i.State, i.OwnerName, i.PetName, i.Phone,
                i.Total, i.Due,
                AvatarTones[Math.Abs((i.PetName ?? "x").GetHashCode()) % AvatarTones.Length],
                FirstChar(i.OwnerName)))
            .ToList();

        return Task.FromResult(list);
    }

    public Task<InvoiceDetail?> GetInvoiceAsync(string invoiceId)
    {
        var inv = Find(invoiceId);
        return Task.FromResult(inv is null ? null : MapDetail(inv));
    }

    public Task<InvoiceDetail> CreateFromEncounterAsync(string encounterId)
    {
        var enc = _encounters.FirstOrDefault(e => e.Id == encounterId)
            ?? throw new InvalidOperationException($"Unknown encounter: {encounterId}");

        var record = NewRecord(
            $"INV-{_invoiceCounter++:D3}", InvoiceKind.Visit, enc.Id,
            enc.OwnerName, enc.PetName, "",
            enc.Lines.Select(l => new LineRecord(l.Ref, l.Kind, l.Title, l.Quantity, l.UnitPrice)).ToList(),
            DiscountKind.Percent, 0, null, InvoiceState.Draft, 0, []);
        record.DoctorName = enc.DoctorName;
        record.CompletedAt = enc.CompletedAt;

        _invoices.Insert(0, record);
        RecalcAll();
        return Task.FromResult(MapDetail(record));
    }

    public Task<InvoiceDetail> CreateBlankAsync(InvoiceKind kind)
    {
        var record = NewRecord(
            $"INV-{_invoiceCounter++:D3}", kind, null, "", "", "",
            [], DiscountKind.Percent, 0, null, InvoiceState.Draft, 0, []);

        _invoices.Insert(0, record);
        RecalcAll();
        return Task.FromResult(MapDetail(record));
    }

    public Task<IReadOnlyList<InsurerRef>> GetInsurersAsync() =>
        Task.FromResult<IReadOnlyList<InsurerRef>>(Insurers);

    public Task<SaleKpiSnapshot> GetKpisAsync()
    {
        var open = _invoices.Where(i => i.State is InvoiceState.Draft or InvoiceState.Partial).ToList();
        var settled = _invoices.Where(i => i.State == InvoiceState.Settled).ToList();

        var snapshot = new SaleKpiSnapshot(
            settled.Sum(i => i.Paid),
            open.Sum(i => i.Due),
            open.Count,
            _encounters.Count(e => !_invoices.Any(i => i.EncounterId == e.Id)));

        return Task.FromResult(snapshot);
    }

    private InvoiceRecord? Find(string invoiceId) =>
        _invoices.FirstOrDefault(i => i.Id == invoiceId);

    private InvoiceRecord NewRecord(
        string id, InvoiceKind kind, string? encounterId,
        string owner, string pet, string phone,
        List<LineRecord> lines, DiscountKind discountKind, int discountValue,
        string? insuranceOrgId, InvoiceState state, long paid, List<PaymentRecord> payments)
    {
        return new InvoiceRecord
        {
            Id = id,
            Kind = kind,
            EncounterId = encounterId,
            OwnerName = owner,
            PetName = pet,
            Phone = phone,
            Lines = lines,
            DiscountKind = discountKind,
            DiscountValue = discountValue,
            InsuranceOrgId = insuranceOrgId,
            State = state,
            Paid = paid,
            Payments = payments,
        };
    }

    private void RecalcAll()
    {
        foreach (var inv in _invoices)
        {
            Calc(inv);
        }
    }

    /// <summary>
    /// Mirrors <c>FLOW.calcInvoice</c> (§8.2 order): line-sum base (services
    /// only feed tax), invoice discount, tax on the discounted service share,
    /// then insurance share of the after-discount total, then total and due.
    /// </summary>
    private static void Calc(InvoiceRecord inv)
    {
        long baseTotal = 0, svcBase = 0;
        foreach (var ln in inv.Lines)
        {
            var sub = (long)Math.Round(ln.UnitPrice * ln.Quantity * (100 - ln.DiscountPercent) / 100.0);
            baseTotal += sub;
            if (ln.Kind == InvoiceLineKind.Service)
            {
                svcBase += sub;
            }
        }

        long discountAmount = inv.DiscountKind == DiscountKind.Amount
            ? Math.Min(inv.DiscountValue, baseTotal)
            : (long)Math.Round(baseTotal * inv.DiscountValue / 100.0);

        var afterDiscount = Math.Max(0, baseTotal - discountAmount);

        long svcAfter = baseTotal > 0 ? (long)Math.Round(svcBase * (double)afterDiscount / baseTotal) : 0;
        var tax = (long)Math.Round(svcAfter * (inv.TaxPercent) / 100.0);

        long insShare = 0;
        if (inv.InsuranceOrgId is not null)
        {
            var org = Insurers.FirstOrDefault(o => o.Id == inv.InsuranceOrgId);
            if (org is not null)
            {
                insShare = (long)Math.Round(afterDiscount * org.Percent / 100.0);
            }
        }

        inv.Base = baseTotal;
        inv.DiscountAmount = discountAmount;
        inv.Tax = tax;
        inv.InsuranceShare = insShare;
        inv.Total = Math.Max(0, afterDiscount + tax - insShare);
        inv.Due = Math.Max(0, inv.Total - inv.Paid);
    }

    private static InvoiceDetail MapDetail(InvoiceRecord inv)
    {
        var isLocked = inv.State is InvoiceState.Settled or InvoiceState.Void or InvoiceState.Refunded;
        var stateLabel = inv.State switch
        {
            InvoiceState.Draft => "پیش‌نویس",
            InvoiceState.Partial => "تسویه جزئی",
            InvoiceState.Settled => "تسویه کامل",
            InvoiceState.Void => "ابطال شده",
            InvoiceState.Refunded => "مسترد شده",
            _ => "—",
        };
        var sourceLabel = inv.Kind == InvoiceKind.Visit ? "از ویزیت" : "فروش";
        var isGuest = string.IsNullOrEmpty(inv.OwnerName);

        var lines = inv.Lines
            .Select((ln, idx) => new InvoiceLine(
                idx, ln.Title, ln.Kind, ln.UnitPrice, ln.Quantity, ln.DiscountPercent,
                ln.IsCustom, ln.Ref, IconFor(ln),
                ln.Kind == InvoiceLineKind.Service ? "خدمت" : "کالا"))
            .ToList();

        var payments = inv.Payments
            .Select(p => new InvoicePayment(p.Method, MethodLabel(p.Method), p.Note, p.At, p.Amount))
            .ToList();

        var insuranceOrg = inv.InsuranceOrgId is null
            ? null
            : Insurers.FirstOrDefault(o => o.Id == inv.InsuranceOrgId);
        var insuranceLabel = insuranceOrg is null
            ? "بدون بیمه"
            : $"{insuranceOrg.Name} ({PersianNumberFormatter.ToPersianDigits(insuranceOrg.Percent)})";

        return new InvoiceDetail(
            inv.Id, inv.Kind, inv.State, stateLabel, sourceLabel,
            inv.OwnerName, inv.PetName, inv.Phone, isGuest, isLocked,
            AvatarTones[Math.Abs((inv.PetName ?? "x").GetHashCode()) % AvatarTones.Length],
            FirstChar(inv.OwnerName),
            inv.EncounterId is not null, inv.DoctorName, inv.CompletedAt,
            inv.InsuranceOrgId ?? string.Empty,
            insuranceLabel,
            lines, inv.DiscountKind, inv.DiscountValue, payments,
            inv.Base, inv.DiscountAmount, inv.TaxPercent, inv.Tax, inv.InsuranceShare,
            inv.Total, inv.Paid, inv.Due,
            inv.InsuranceShare > 0, inv.DiscountAmount > 0);
    }

    private static string IconFor(LineRecord ln)
    {
        if (ln.IsCustom)
        {
            return "IconEdit";
        }

        var map = ServiceIcons.GetValueOrDefault(ln.Ref) ?? ItemIcons.GetValueOrDefault(ln.Ref);
        return map ?? (ln.Kind == InvoiceLineKind.Service ? "IconClipboard" : "IconBox");
    }

    private static string MethodLabel(PaymentMethod method) => method switch
    {
        PaymentMethod.Cash => "نقدی",
        PaymentMethod.Card => "کارت",
        PaymentMethod.Cheque => "چک",
        PaymentMethod.Credit => "نسیه",
        PaymentMethod.Mixed => "ترکیبی",
        _ => "—",
    };

    private static string FirstChar(string? value) =>
        string.IsNullOrEmpty(value) ? "?" : value[..1];

    // Catalog ref → icon key, mirroring sale-page.js CAT_ICON.
    private static readonly Dictionary<string, string> ServiceIcons = new()
    {
        ["sv-1"] = "IconClipboard",
        ["sv-2"] = "IconSyringe",
        ["sv-3"] = "IconTools",
        ["sv-4"] = "IconScissors",
        ["sv-5"] = "IconBath",
        ["sv-6"] = "IconFlask",
        ["sv-7"] = "IconImage",
        ["sv-8"] = "IconImage",
        ["sv-9"] = "IconClipboard",
        ["sv-10"] = "IconSyringe",
        ["sv-11"] = "IconHome",
        ["sv-12"] = "IconClipboard",
    };

    private static readonly Dictionary<string, string> ItemIcons = new()
    {
        ["it-1"] = "IconBone",
        ["it-2"] = "IconBone",
        ["it-3"] = "IconBone",
        ["it-4"] = "IconBath",
        ["it-5"] = "IconShield",
        ["it-6"] = "IconBone",
        ["it-7"] = "IconScissors",
        ["it-8"] = "IconBox",
        ["it-9"] = "IconFlask",
        ["it-10"] = "IconShield",
    };

    private sealed record ReadyEncounter(
        string Id, string TicketId, string OwnerName, string PetName, string PetKind,
        string DoctorShort, string DoctorName, string CompletedAt, List<LineRecord> Lines);

    private sealed record LineRecord(
        string Ref, InvoiceLineKind Kind, string Title, int Quantity, long UnitPrice,
        int DiscountPercent = 0, bool IsCustom = false);

    private sealed record PaymentRecord(
        PaymentMethod Method, string Note, string At, long Amount);

    private sealed class InvoiceRecord
    {
        public required string Id { get; init; }

        public required InvoiceKind Kind { get; init; }

        public string? EncounterId { get; init; }

        public required string OwnerName { get; init; }

        public required string PetName { get; init; }

        public required string Phone { get; init; }

        public required List<LineRecord> Lines { get; init; }

        public required DiscountKind DiscountKind { get; init; }

        public required int DiscountValue { get; init; }

        public string? InsuranceOrgId { get; init; }

        public required InvoiceState State { get; init; }

        public required long Paid { get; init; }

        public required List<PaymentRecord> Payments { get; init; }

        public string DoctorName { get; set; } = string.Empty;

        public string CompletedAt { get; set; } = string.Empty;

        public long Base { get; set; }

        public long DiscountAmount { get; set; }

        public int TaxPercent { get; init; } = 9;

        public long Tax { get; set; }

        public long InsuranceShare { get; set; }

        public long Total { get; set; }

        public long Due { get; set; }
    }
}
