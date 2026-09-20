using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Animora.Contracts.Cashier;
using Animora.Desktop.Localization;

namespace Animora.Desktop.Mocks;

/// <summary>
/// Deterministic in-memory implementation of <see cref="ICashierService"/> — the
/// single swappable seam a future backend replaces (R-18). Its sample data is
/// re-derived from what the reference (<c>Animora-UI/assets/cashier.js</c> +
/// <c>clinic-data.js</c>) renders: a spread of invoices across every state,
/// each with 1–4 lines and 0–2 payment-history entries, carrying the same
/// line/total arithmetic as <c>FLOW.calcInvoice</c> (§8.2 order: base → discount
/// → tax-on-services → insurance share → total → due). It is stateful: the
/// pay/void/refund/edit mutations persist so navigation preserves them.
/// </summary>
public sealed class MockCashierService : ICashierService
{
    private static readonly string[] AvatarTones = ["a", "b", "c", "d", "e", "f"];

    private static readonly CashierInsurer[] Insurers =
    [
        new("organ-1", "بیمه آسیا — پوشش حیوانات خانگی", 15),
        new("organ-2", "بیمه پاسارگاد — پوشش درمان", 20),
        new("organ-3", "بیمه دانا — پوشش جامع", 25),
        new("organ-4", "بیمه ملت — پوشش پایه", 10),
    ];

    // Catalog offered by the detail's add-line select, mirroring the reference's
    // SERVICES (sv-1..sv-12) + ITEMS (it-1..it-10).
    private static readonly CashierCatalogItem[] Catalog =
    [
        new("sv-1",  CashierLineKind.Service, "ویزیت عمومی",              250_000),
        new("sv-2",  CashierLineKind.Service, "واکسن چهارگانه برونشیت",   380_000),
        new("sv-3",  CashierLineKind.Service, "جراحی عقیم‌سازی",           2_800_000),
        new("sv-4",  CashierLineKind.Service, "دندان‌پزشکی و جرم‌گیری",     1_200_000),
        new("sv-5",  CashierLineKind.Service, "آرایش و پیرایش",            650_000),
        new("sv-6",  CashierLineKind.Service, "آزمایش خون کامل",           800_000),
        new("sv-7",  CashierLineKind.Service, "سونوگرافی شکمی",            1_500_000),
        new("sv-8",  CashierLineKind.Service, "رادیولوژی",                 900_000),
        new("sv-9",  CashierLineKind.Service, "ویزیت تخصصی پوست",          350_000),
        new("sv-10", CashierLineKind.Service, "تزریق و سرم‌تراپی",          450_000),
        new("sv-11", CashierLineKind.Service, "بستری شبانه‌روزی",          1_000_000),
        new("sv-12", CashierLineKind.Service, "معاینه پیگیری",             180_000),
        new("it-1",  CashierLineKind.Item,    "قرص ضد انگل",               160_000),
        new("it-2",  CashierLineKind.Item,    "غذای کنسرو مرغ",            85_000),
        new("it-3",  CashierLineKind.Item,    "غذای خشک بالغ سگ (۵ کیلو)",  850_000),
        new("it-4",  CashierLineKind.Item,    "شامپوی مخصوص حیوانات",      220_000),
        new("it-5",  CashierLineKind.Item,    "مکمل تقویت مفصل",           450_000),
        new("it-6",  CashierLineKind.Item,    "آنتی‌بیوتیک خوراکی",          320_000),
        new("it-7",  CashierLineKind.Item,    "برس و شانه پاکسازی",        125_000),
        new("it-8",  CashierLineKind.Item,    "خاک مخصوص گربه",            280_000),
        new("it-9",  CashierLineKind.Item,    "ویتامین مایع",              150_000),
        new("it-10", CashierLineKind.Item,    "قلاده ضدکک",                390_000),
    ];

    private readonly List<InvoiceRecord> _invoices = [];

    // O(1) id -> list-index lookups (PERFORMANCE.md §2/§13). Invoices are only
    // ever appended in this mock (never inserted/removed), so the index is
    // built once at construction and simply extended on append.
    private readonly Dictionary<string, int> _invoiceIndexById = new(StringComparer.Ordinal);

    // Static lookups for the fixed catalog/insurer arrays, avoiding a
    // FirstOrDefault scan on every add-line / insurance-share calculation.
    private static readonly Dictionary<string, CashierCatalogItem> CatalogById =
        Catalog.ToDictionary(c => c.Id, StringComparer.Ordinal);

    private static readonly Dictionary<string, CashierInsurer> InsurersById =
        Insurers.ToDictionary(i => i.Id, StringComparer.Ordinal);

    public MockCashierService()
    {
        var today = JalaliDate.Today.ToString("short");

        // INV-001 — draft built from a visit, with discount + insurance
        // (transcribed from the reference's INV-001).
        var inv1 = NewRecord("INV-001", "مریم آذری", "کوکو", "۰۹۱۰۵۵۶۶۷۷۸", today,
            [new("sv-1", CashierLineKind.Service, "ویزیت عمومی", 1, 250_000),
             new("it-1", CashierLineKind.Item, "قرص ضد انگل", 2, 160_000)],
            CashierDiscountKind.Percent, 10, "organ-2", CashierInvoiceState.Draft, 0, []);

        // INV-002 — partial direct sale, one cash prepayment (reference INV-002).
        var inv2 = NewRecord("INV-002", "سارا یوسفی", "آبی", "۰۹۱۹۸۷۶۵۴۳۲", today,
            [new("sv-9", CashierLineKind.Service, "ویزیت تخصصی پوست", 1, 350_000),
             new("it-5", CashierLineKind.Item, "مکمل تقویت مفصل", 1, 450_000)],
            CashierDiscountKind.Percent, 0, null, CashierInvoiceState.Partial, 400_000,
            [new(CashierPaymentMethod.Cash, null, "پیش‌پرداخت نقدی", "11:20", 400_000)]);

        // INV-003 — settled visit invoice, card payment.
        var inv3 = NewRecord("INV-003", "رضا کریمی", "میلو", "۰۹۳۰۴۴۵۵۶۶۷", today,
            [new("sv-5", CashierLineKind.Service, "آرایش و پیرایش", 1, 650_000),
             new("it-2", CashierLineKind.Item, "غذای کنسرو مرغ", 2, 85_000)],
            CashierDiscountKind.Percent, 0, null, CashierInvoiceState.Settled, 921_000,
            [new(CashierPaymentMethod.Card, null, "پرداخت کارتی", "12:40", 921_000)]);

        // INV-004 — voided walk-in invoice.
        var inv4 = NewRecord("INV-004", "الهام صادقی", "نانا", "۰۹۱۲۷۷۸۸۹۹۰", today,
            [new("it-8", CashierLineKind.Item, "خاک مخصوص گربه", 1, 280_000),
             new("it-10", CashierLineKind.Item, "قلاده ضدکک", 1, 390_000)],
            CashierDiscountKind.Percent, 0, null, CashierInvoiceState.Void, 0, []);

        // INV-005 — refunded settled invoice with a negative payment row.
        var inv5 = NewRecord("INV-005", "مریم آذری", "کوکو", "۰۹۱۰۵۵۶۶۷۷۸", today,
            [new("it-3", CashierLineKind.Item, "غذای خشک بالغ سگ (۵ کیلو)", 1, 850_000)],
            CashierDiscountKind.Percent, 0, null, CashierInvoiceState.Refunded, 0,
            [new(CashierPaymentMethod.Cash, null, "پرداخت نقدی", "13:10", 926_500),
             new(CashierPaymentMethod.Cash, null, "عودت وجه", "13:35", -926_500)]);

        // INV-006 — draft with a cheque payment pending (partial).
        var inv6 = NewRecord("INV-006", "نگار محمدی", "پشمک", "۰۹۱۲۱۲۳۴۵۶۷", today,
            [new("sv-2", CashierLineKind.Service, "واکسن چهارگانه برونشیت", 1, 380_000),
             new("sv-1", CashierLineKind.Service, "ویزیت عمومی", 1, 250_000),
             new("it-9", CashierLineKind.Item, "ویتامین مایع", 1, 150_000)],
            CashierDiscountKind.Percent, 0, null, CashierInvoiceState.Partial, 300_000,
            [new(CashierPaymentMethod.Cheque, "۴۵۸۹۱۲۳", "چک تأمین‌کننده", "14:05", 300_000)]);

        // INV-007 — settled with per-line discount + insurance.
        var inv7 = NewRecord("INV-007", "امیر رستگار", "لئو", "۰۹۱۲۴۴۴۵۵۶۶", today,
            [new("sv-3", CashierLineKind.Service, "جراحی عقیم‌سازی", 1, 2_800_000, 5),
             new("it-6", CashierLineKind.Item, "آنتی‌بیوتیک خوراکی", 1, 320_000)],
            CashierDiscountKind.Amount, 100_000, "organ-3", CashierInvoiceState.Settled, 0,
            []);

        // INV-008 — draft, no payments (open).
        var inv8 = NewRecord("INV-008", "محسن دهقان", "رکس", "۰۹۳۵۵۵۶۶۷۷۸", today,
            [new("sv-4", CashierLineKind.Service, "دندان‌پزشکی و جرم‌گیری", 1, 1_200_000)],
            CashierDiscountKind.Percent, 0, null, CashierInvoiceState.Draft, 0, []);

        _invoices.AddRange([inv1, inv2, inv3, inv4, inv5, inv6, inv7, inv8]);
        RebuildInvoiceIndex();
        RecalcAll();

        // Settled invoices need their paid to equal the recomputed total.
        inv3.Paid = inv3.Total;
        inv7.Paid = inv7.Total;
        RecalcAll();
    }

    private void RebuildInvoiceIndex()
    {
        _invoiceIndexById.Clear();
        for (var i = 0; i < _invoices.Count; i++)
        {
            _invoiceIndexById[_invoices[i].Id] = i;
        }
    }

    public Task<IReadOnlyList<CashierInvoice>> GetInvoicesAsync()
    {
        IReadOnlyList<CashierInvoice> list = _invoices
            .Select(i => new CashierInvoice(
                i.Id, i.State, StateLabel(i.State), i.OwnerName, i.PetName, i.Date,
                i.Total, i.Paid, i.Due))
            .ToList();

        return Task.FromResult(list);
    }

    public Task<CashierInvoiceDetail?> GetDetailAsync(string invoiceId)
    {
        var inv = Find(invoiceId);
        return Task.FromResult(inv is null ? null : MapDetail(inv));
    }

    public Task<IReadOnlyList<CashierStat>> GetStatsAsync()
    {
        var all = _invoices;
        var open = all.Where(i => i.State is CashierInvoiceState.Draft or CashierInvoiceState.Partial).ToList();
        var settled = all.Where(i => i.State == CashierInvoiceState.Settled).ToList();
        var sumDue = open.Sum(i => i.Due);
        var sumPaid = settled.Sum(i => i.Paid);
        var sumToday = all
            .Where(i => i.Payments.Count > 0)
            .Sum(i => i.Payments.Where(p => p.Amount > 0).Sum(p => p.Amount));

        IReadOnlyList<CashierStat> stats =
        [
            new("warn", "IconWallet", "مطالبات باز", sumDue, "جمع ماندهٔ فاکتورهای پیش‌نویس/جزئی", true),
            new("ok", "IconCheck", "تسویه‌های امروز", sumPaid, "مجموع فاکتورهای تسویه‌شده", true),
            new("info", "IconReceipt", "جریان نقدی روز", sumToday, "مجموع پرداخت‌های ثبت‌شده", true),
            new("lilac", "IconReceipt", "تعداد فاکتورها", all.Count, "پیش‌نویس / جزئی / تسویه / ابطال / مسترد", false),
        ];

        return Task.FromResult(stats);
    }

    public Task<IReadOnlyList<CashierTabCount>> GetTabCountsAsync()
    {
        var all = _invoices;
        IReadOnlyList<CashierTabCount> counts =
        [
            new(CashierTab.Open, "باز", all.Count(i => i.State is CashierInvoiceState.Draft or CashierInvoiceState.Partial)),
            new(CashierTab.Settled, "تسویه‌شده", all.Count(i => i.State == CashierInvoiceState.Settled)),
            new(CashierTab.Void, "ابطال‌شده", all.Count(i => i.State is CashierInvoiceState.Void or CashierInvoiceState.Refunded)),
            new(CashierTab.All, "همه", all.Count),
        ];

        return Task.FromResult(counts);
    }

    public Task<IReadOnlyList<CashierInsurer>> GetInsurersAsync() =>
        Task.FromResult<IReadOnlyList<CashierInsurer>>(Insurers);

    public Task<IReadOnlyList<CashierCatalogItem>> GetCatalogItemsAsync() =>
        Task.FromResult<IReadOnlyList<CashierCatalogItem>>(Catalog);

    public Task<CashierActionResult> PayAsync(string invoiceId, CashierPayRequest request)
    {
        var inv = Find(invoiceId);
        if (inv is null)
        {
            return Task.FromResult(new CashierActionResult(false, "فاکتور یافت نشد.", null));
        }

        if (inv.State is CashierInvoiceState.Void or CashierInvoiceState.Refunded)
        {
            return Task.FromResult(new CashierActionResult(false, "این فاکتور قابلیت پرداخت ندارد.", MapDetail(inv)));
        }

        if (inv.State == CashierInvoiceState.Settled)
        {
            return Task.FromResult(new CashierActionResult(false, "این فاکتور قبلاً تسویه شده است.", MapDetail(inv)));
        }

        var amount = Math.Max(0, request.Amount);
        if (amount <= 0)
        {
            return Task.FromResult(new CashierActionResult(false, "مبلغ پرداخت باید بزرگ‌تر از صفر باشد.", MapDetail(inv)));
        }

        var capped = Math.Min(amount, inv.Due);
        inv.Paid += capped;
        inv.Payments.Add(new PaymentRecord(
            request.Method,
            string.IsNullOrWhiteSpace(request.Reference) ? null : request.Reference,
            string.IsNullOrWhiteSpace(request.Note) ? "پرداخت از صندوق" : request.Note,
            NowStamp(), capped));

        Calc(inv);
        inv.State = inv.Paid >= inv.Total ? CashierInvoiceState.Settled : CashierInvoiceState.Partial;

        return Task.FromResult(new CashierActionResult(true, null, MapDetail(inv)));
    }

    public Task<CashierActionResult> VoidAsync(string invoiceId)
    {
        var inv = Find(invoiceId);
        if (inv is null)
        {
            return Task.FromResult(new CashierActionResult(false, "فاکتور یافت نشد.", null));
        }

        if (inv.State is CashierInvoiceState.Settled or CashierInvoiceState.Refunded)
        {
            return Task.FromResult(new CashierActionResult(
                false, "فاکتور تسویه‌شده یا مستردشده قابل ابطال نیست.", MapDetail(inv)));
        }

        inv.State = CashierInvoiceState.Void;
        return Task.FromResult(new CashierActionResult(true, null, MapDetail(inv)));
    }

    public Task<CashierActionResult> RefundAsync(string invoiceId, CashierRefundRequest request)
    {
        var inv = Find(invoiceId);
        if (inv is null)
        {
            return Task.FromResult(new CashierActionResult(false, "فاکتور یافت نشد.", null));
        }

        if (inv.State is not (CashierInvoiceState.Partial or CashierInvoiceState.Settled))
        {
            return Task.FromResult(new CashierActionResult(
                false, "عودت فقط روی فاکتور پرداخت‌شده (جزئی یا کامل) ممکن است.", MapDetail(inv)));
        }

        var amount = Math.Max(0, request.Amount);
        if (amount <= 0 || amount > inv.Paid)
        {
            return Task.FromResult(new CashierActionResult(
                false, "مبلغ عودت باید بین ۱ ریال و کل مبلغ پرداخت‌شده باشد.", MapDetail(inv)));
        }

        inv.Paid = Math.Max(0, inv.Paid - amount);
        inv.Payments.Add(new PaymentRecord(CashierPaymentMethod.Cash, null, "عودت وجه", NowStamp(), -amount));
        inv.State = CashierInvoiceState.Refunded;
        Calc(inv);

        return Task.FromResult(new CashierActionResult(true, null, MapDetail(inv)));
    }

    public Task<CashierInvoiceDetail> UpdateLineAsync(string invoiceId, int lineIndex, int quantity, int discountPercent)
    {
        var inv = Find(invoiceId) ?? throw new InvalidOperationException($"Unknown invoice: {invoiceId}");
        if (lineIndex >= 0 && lineIndex < inv.Lines.Count)
        {
            var ln = inv.Lines[lineIndex];
            inv.Lines[lineIndex] = ln with
            {
                Quantity = Math.Max(1, quantity),
                DiscountPercent = Math.Clamp(discountPercent, 0, 100),
            };
        }

        Calc(inv);
        return Task.FromResult(MapDetail(inv));
    }

    public Task<CashierInvoiceDetail> RemoveLineAsync(string invoiceId, int lineIndex)
    {
        var inv = Find(invoiceId) ?? throw new InvalidOperationException($"Unknown invoice: {invoiceId}");
        if (lineIndex >= 0 && lineIndex < inv.Lines.Count)
        {
            inv.Lines.RemoveAt(lineIndex);
        }

        Calc(inv);
        return Task.FromResult(MapDetail(inv));
    }

    public Task<CashierInvoiceDetail> AddLineAsync(string invoiceId, string catalogItemId)
    {
        var inv = Find(invoiceId) ?? throw new InvalidOperationException($"Unknown invoice: {invoiceId}");
        var cat = CatalogById.GetValueOrDefault(catalogItemId);
        if (cat is not null)
        {
            var existing = inv.Lines.FirstOrDefault(l => l.Ref == cat.Id);
            if (existing is not null)
            {
                var idx = inv.Lines.IndexOf(existing);
                inv.Lines[idx] = existing with { Quantity = existing.Quantity + 1 };
            }
            else
            {
                inv.Lines.Add(new LineRecord(cat.Id, cat.Kind, cat.Title, 1, cat.Price));
            }
        }

        Calc(inv);
        return Task.FromResult(MapDetail(inv));
    }

    public Task<CashierInvoiceDetail> SetDiscountAsync(string invoiceId, CashierDiscountKind kind, int value)
    {
        var inv = Find(invoiceId) ?? throw new InvalidOperationException($"Unknown invoice: {invoiceId}");
        inv.DiscountKind = kind;
        inv.DiscountValue = Math.Max(0, value);
        Calc(inv);
        return Task.FromResult(MapDetail(inv));
    }

    public Task<CashierInvoiceDetail> SetInsuranceAsync(string invoiceId, string? insurerId)
    {
        var inv = Find(invoiceId) ?? throw new InvalidOperationException($"Unknown invoice: {invoiceId}");
        inv.InsuranceOrgId = string.IsNullOrEmpty(insurerId) ? null : insurerId;
        Calc(inv);
        return Task.FromResult(MapDetail(inv));
    }

    public Task<CashierInvoiceDetail> RecalculateAsync(string invoiceId)
    {
        var inv = Find(invoiceId) ?? throw new InvalidOperationException($"Unknown invoice: {invoiceId}");
        Calc(inv);
        return Task.FromResult(MapDetail(inv));
    }

    private InvoiceRecord? Find(string invoiceId) =>
        _invoiceIndexById.TryGetValue(invoiceId, out var index) ? _invoices[index] : null;

    private InvoiceRecord NewRecord(
        string id, string owner, string pet, string phone, string date,
        List<LineRecord> lines, CashierDiscountKind discountKind, int discountValue,
        string? insuranceOrgId, CashierInvoiceState state, long paid, List<PaymentRecord> payments)
    {
        return new InvoiceRecord
        {
            Id = id,
            OwnerName = owner,
            PetName = pet,
            Phone = phone,
            Date = date,
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
            if (ln.Kind == CashierLineKind.Service)
            {
                svcBase += sub;
            }
        }

        long discountAmount = inv.DiscountKind == CashierDiscountKind.Amount
            ? Math.Min(inv.DiscountValue, baseTotal)
            : (long)Math.Round(baseTotal * inv.DiscountValue / 100.0);

        var afterDiscount = Math.Max(0, baseTotal - discountAmount);

        long svcAfter = baseTotal > 0 ? (long)Math.Round(svcBase * (double)afterDiscount / baseTotal) : 0;
        var tax = (long)Math.Round(svcAfter * inv.TaxPercent / 100.0);

        long insShare = 0;
        if (inv.InsuranceOrgId is not null)
        {
            var org = InsurersById.GetValueOrDefault(inv.InsuranceOrgId);
            if (org is not null)
            {
                insShare = (long)Math.Round(afterDiscount * org.Percent / 100.0);
            }
        }

        inv.Base = baseTotal;
        inv.DiscountAmount = discountAmount;
        inv.AfterDiscount = afterDiscount;
        inv.Tax = tax;
        inv.InsuranceShare = insShare;
        inv.Total = Math.Max(0, afterDiscount + tax - insShare);
        inv.Due = Math.Max(0, inv.Total - inv.Paid);
    }

    private static CashierInvoiceDetail MapDetail(InvoiceRecord inv)
    {
        var editable = inv.State is CashierInvoiceState.Draft or CashierInvoiceState.Partial;

        var lines = inv.Lines
            .Select((ln, idx) => new CashierInvoiceLine(
                idx, ln.Kind, ln.Title, ln.UnitPrice, ln.Quantity, ln.DiscountPercent))
            .ToList();

        var payments = inv.Payments
            .Select(p => new CashierPayment(p.Method, MethodLabel(p.Method), p.Reference ?? string.Empty, p.Note, p.At, p.Amount))
            .ToList();

        return new CashierInvoiceDetail(
            inv.Id, inv.State, StateLabel(inv.State),
            inv.OwnerName, inv.PetName, inv.Phone, inv.Date,
            editable, inv.InsuranceOrgId ?? string.Empty,
            inv.DiscountKind, inv.DiscountValue, lines, payments,
            inv.Base, inv.DiscountAmount, inv.AfterDiscount, inv.TaxPercent, inv.Tax,
            inv.InsuranceShare, inv.Total, inv.Paid, inv.Due);
    }

    private static string StateLabel(CashierInvoiceState state) => state switch
    {
        CashierInvoiceState.Draft => "پیش‌نویس",
        CashierInvoiceState.Partial => "تسویه جزئی",
        CashierInvoiceState.Settled => "تسویه شد",
        CashierInvoiceState.Void => "ابطال",
        CashierInvoiceState.Refunded => "عودت",
        _ => "—",
    };

    private static string MethodLabel(CashierPaymentMethod method) => method switch
    {
        CashierPaymentMethod.Cash => "نقدی",
        CashierPaymentMethod.Card => "کارت",
        CashierPaymentMethod.Cheque => "چک",
        CashierPaymentMethod.Credit => "نسیه",
        CashierPaymentMethod.Mixed => "ترکیبی",
        _ => "—",
    };

    private static string NowStamp() =>
        PersianNumberFormatter.ToPersianDigits($"{System.DateTime.Now.Hour:D2}:{System.DateTime.Now.Minute:D2}");

    private sealed record LineRecord(
        string Ref, CashierLineKind Kind, string Title, int Quantity, long UnitPrice,
        int DiscountPercent = 0);

    private sealed record PaymentRecord(
        CashierPaymentMethod Method, string? Reference, string Note, string At, long Amount);

    private sealed class InvoiceRecord
    {
        public required string Id { get; init; }

        public required string OwnerName { get; set; }

        public required string PetName { get; set; }

        public required string Phone { get; set; }

        public required string Date { get; init; }

        public required List<LineRecord> Lines { get; init; }

        public required CashierDiscountKind DiscountKind { get; set; }

        public required int DiscountValue { get; set; }

        public string? InsuranceOrgId { get; set; }

        public required CashierInvoiceState State { get; set; }

        public required long Paid { get; set; }

        public required List<PaymentRecord> Payments { get; init; }

        public int TaxPercent { get; init; } = 9;

        public long Base { get; set; }

        public long DiscountAmount { get; set; }

        public long AfterDiscount { get; set; }

        public long Tax { get; set; }

        public long InsuranceShare { get; set; }

        public long Total { get; set; }

        public long Due { get; set; }
    }
}
