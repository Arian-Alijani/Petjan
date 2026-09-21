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

    // Cashier-history ledger cache. Built lazily on the first query and rebuilt
    // only after a mutation (MarkHistoryDirty), so repeated paging/sorting does
    // not re-derive the rows (PERFORMANCE.md §13).
    private IReadOnlyList<LedgerRow>? _ledgerRows;
    private bool _historyDirty = true;

    private static readonly InsurerRef[] Insurers =
    [
        new("organ-1", "بیمه آسیا — پوشش حیوانات خانگی", 15),
        new("organ-2", "بیمه پاسارگاد — پوشش درمان", 20),
        new("organ-3", "بیمه دانا — پوشش جامع", 25),
        new("organ-4", "بیمه ملت — پوشش پایه", 10),
    ];

    private static readonly string[] AvatarTones = ["a", "b", "c", "d", "e", "f"];

    // Catalog, mirroring the reference's SERVICES (sv-1..sv-12) + ITEMS
    // (it-1..it-10) with their SVC_GROUPS / ITEM_GROUPS labels + CAT_ICON keys.
    private static readonly CatalogItem[] Catalog =
    [
        new("sv-1",  "svc",  "ویزیت عمومی",              "ویزیت",          "consult", 250_000,  "IconClipboard"),
        new("sv-2",  "svc",  "واکسن چهارگانه برونشیت",   "واکسن",          "vaccine", 380_000,  "IconSyringe"),
        new("sv-3",  "svc",  "جراحی عقیم‌سازی",           "جراحی",          "surgery", 2_800_000, "IconTools"),
        new("sv-4",  "svc",  "دندان‌پزشکی و جرم‌گیری",     "دندان",          "dental",  1_200_000, "IconScissors"),
        new("sv-5",  "svc",  "آرایش و پیرایش",           "گرومینگ",        "groom",   650_000,  "IconBath"),
        new("sv-6",  "svc",  "آزمایش خون کامل",          "آزمایشگاه",       "lab",     800_000,  "IconFlask"),
        new("sv-7",  "svc",  "سونوگرافی شکمی",           "تصویربرداری",     "img",     1_500_000, "IconImage"),
        new("sv-8",  "svc",  "رادیولوژی",                "تصویربرداری",     "img",     900_000,  "IconImage"),
        new("sv-9",  "svc",  "ویزیت تخصصی پوست",         "ویزیت",          "consult", 350_000,  "IconClipboard"),
        new("sv-10", "svc",  "تزریق و سرم‌تراپی",          "تزریق",          "inject",  450_000,  "IconSyringe"),
        new("sv-11", "svc",  "بستری شبانه‌روزی",          "بستری",          "stay",    1_000_000, "IconHome"),
        new("sv-12", "svc",  "معاینه پیگیری",            "ویزیت",          "consult", 180_000,  "IconClipboard"),
        new("it-1",  "item", "قرص ضد انگل",              "دارو",           "drug",    160_000,  "IconBone"),
        new("it-2",  "item", "غذای کنسرو مرغ",           "غذا",            "food",    85_000,   "IconBone"),
        new("it-3",  "item", "غذای خشک بالغ سگ (۵ کیلو)", "غذا",            "food",    850_000,  "IconBone"),
        new("it-4",  "item", "شامپوی مخصوص حیوانات",     "بهداشتی",         "hygiene", 220_000,  "IconBath"),
        new("it-5",  "item", "مکمل تقویت مفصل",          "مکمل",           "suppl",   450_000,  "IconShield"),
        new("it-6",  "item", "آنتی‌بیوتیک خوراکی",         "دارو",           "drug",    320_000,  "IconBone"),
        new("it-7",  "item", "برس و شانه پاکسازی",       "بهداشتی",         "hygiene", 125_000,  "IconScissors"),
        new("it-8",  "item", "خاک مخصوص گربه",           "مصرفی",          "consum",  280_000,  "IconBox"),
        new("it-9",  "item", "ویتامین مایع",             "دارو",           "drug",    150_000,  "IconFlask"),
        new("it-10", "item", "قلاده ضدکک",               "ضدانگل",          "antip",   390_000,  "IconShield"),
    ];

    // Owner directory for «انتخاب مشتری» (mirrors window.OWNERS + first pet).
    private static readonly OwnerRef[] Owners =
    [
        new("ow-1", "نگار", "محمدی",  "۷۲-۱۰۱۲", "۰۹۱۲۱۲۳۴۵۶۷", "a", 0,        "پشمک"),
        new("ow-2", "امیر", "رستگار", "۷۲-۱۰۳۴", "۰۹۱۲۴۴۴۵۵۶۶", "b", 1_250_000, "لئو"),
        new("ow-3", "سارا", "یوسفی",  "۷۲-۱۰۵۸", "۰۹۳۵۷۷۷۸۸۹۹", "c", 0,        "آبی"),
        new("ow-4", "علی",  "نوروزی", "۷۲-۱۰۷۷", "۰۹۱۹۲۲۲۳۳۴۴", "d", 0,        ""),
        new("ow-5", "آرمان", "توسلی", "۷۲-۱۰۹۱", "۰۹۳۳۳۳۳۴۴۵۵", "e", 0,        ""),
        new("ow-6", "مهسا", "کریمی",  "۷۲-۱۱۰۴", "۰۹۰۲۵۵۵۶۶۷۷", "f", 0,        ""),
    ];


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

        // Spread the seed invoices across recent days so the cashier-history
        // ledger's date-range filter and day grouping have something to show
        // (the reference's synthetic ledger is similarly spread).
        string DaysAgo(int days) => JalaliCalendar.AddDays(JalaliCalendar.Today, -days);
        inv1.Date = DaysAgo(0);
        inv2.Date = DaysAgo(0);
        inv3.Date = DaysAgo(1);
        inv4.Date = DaysAgo(3);
        inv5.Date = DaysAgo(6);

        RecalcAll();
    }

    // ── Phase 21 — catalog picker ───────────────────────────────────────

    // Recent strip seed (mirrors the reference's `state.recent` — most-recent-first).
    private readonly List<RecentItemRef> _recent =
    [
        new("svc", "sv-1"),
        new("item", "it-1"),
        new("svc", "sv-2"),
    ];

    public Task<CatalogSnapshot> GetCatalogAsync()
    {
        var filters = BuildFilters();
        IReadOnlyList<RecentItemRef> recent = _recent.ToList();
        IReadOnlyList<CatalogItem> items = Catalog.ToList();
        return Task.FromResult(new CatalogSnapshot(items, filters, recent));
    }

    public Task<InvoiceDetail> AddLineAsync(string invoiceId, CatalogItem item, int quantity)
    {
        var inv = Find(invoiceId) ?? throw new InvalidOperationException($"Unknown invoice: {invoiceId}");
        var kind = item.Kind == "svc" ? InvoiceLineKind.Service : InvoiceLineKind.Item;
        var existing = inv.Lines.FirstOrDefault(l => l.Ref == item.Id && l.Kind == kind);
        var qty = Math.Max(1, quantity);

        if (existing is not null)
        {
            var idx = inv.Lines.IndexOf(existing);
            inv.Lines[idx] = existing with { Quantity = existing.Quantity + qty };
        }
        else
        {
            inv.Lines.Add(new LineRecord(item.Id, kind, item.Title, qty, item.Price));
        }

        PushRecent(item.Kind, item.Id);
        Log(inv, InvoiceAuditKind.ItemAdded, "افزودن ردیف",
            $"{item.Title} × {PersianNumberFormatter.ToPersianDigits(qty)}");
        Calc(inv);
        MarkHistoryDirty();
        return Task.FromResult(MapDetail(inv));
    }

    public Task<InvoiceDetail> AddCustomLineAsync(string invoiceId, CustomLineRequest request)
    {
        var inv = Find(invoiceId) ?? throw new InvalidOperationException($"Unknown invoice: {invoiceId}");
        var qty = Math.Max(1, request.Quantity);
        inv.Lines.Add(new LineRecord(
            string.Empty, InvoiceLineKind.Service, request.Title,
            qty, Math.Max(0, request.UnitPrice),
            IsCustom: true));

        Log(inv, InvoiceAuditKind.ItemAdded, "افزودن ردیف دلخواه",
            $"{request.Title} × {PersianNumberFormatter.ToPersianDigits(qty)}");
        Calc(inv);
        MarkHistoryDirty();
        return Task.FromResult(MapDetail(inv));
    }

    public Task<PaymentResult> TakePaymentAsync(string invoiceId, PaymentRequest request)
    {
        var inv = Find(invoiceId);
        if (inv is null)
        {
            return Task.FromResult(new PaymentResult(false, "فاکتور یافت نشد.", 0, false, null!));
        }

        if (inv.State is InvoiceState.Void or InvoiceState.Refunded or InvoiceState.Deleted)
        {
            return Task.FromResult(new PaymentResult(false, "این فاکتور قابلیت پرداخت ندارد.", 0, false, MapDetail(inv)));
        }

        if (inv.State == InvoiceState.Settled)
        {
            return Task.FromResult(new PaymentResult(false, "این فاکتور قبلاً تسویه شده است.", 0, false, MapDetail(inv)));
        }

        var amount = Math.Max(0, request.Amount);
        if (amount <= 0)
        {
            return Task.FromResult(new PaymentResult(false, "مبلغ پرداخت باید بزرگ‌تر از صفر باشد.", 0, false, MapDetail(inv)));
        }

        var capped = Math.Min(amount, inv.Due);
        inv.Paid += capped;
        inv.Payments.Add(new PaymentRecord(
            request.Method,
            string.IsNullOrEmpty(request.Note) ? "پرداخت از صندوق" : request.Note,
            NowStamp(), capped));

        Calc(inv);
        inv.State = inv.Paid >= inv.Total ? InvoiceState.Settled : InvoiceState.Partial;
        MarkHistoryDirty();

        return Task.FromResult(new PaymentResult(true, null, capped, inv.State == InvoiceState.Settled, MapDetail(inv)));
    }

    public Task<InvoiceDetail?> VoidAsync(string invoiceId)
    {
        var inv = Find(invoiceId);
        if (inv is null || inv.State is InvoiceState.Settled or InvoiceState.Refunded or InvoiceState.Deleted)
        {
            return Task.FromResult<InvoiceDetail?>(null);
        }

        inv.State = InvoiceState.Void;
        Log(inv, InvoiceAuditKind.Voided, "ابطال فاکتور", "فاکتور از لیست فعال خارج شد");
        MarkHistoryDirty();
        return Task.FromResult<InvoiceDetail?>(MapDetail(inv));
    }

    public Task<RefundResult> RefundAsync(string invoiceId, RefundRequest request)
    {
        var inv = Find(invoiceId);
        if (inv is null)
        {
            return Task.FromResult(new RefundResult(false, "فاکتور یافت نشد.", null!));
        }

        if (inv.State is not (InvoiceState.Partial or InvoiceState.Settled))
        {
            return Task.FromResult(new RefundResult(false, "عودت فقط روی فاکتور پرداخت‌شده (جزئی یا کامل) ممکن است.", MapDetail(inv)));
        }

        var amount = Math.Max(0, request.Amount);
        if (amount <= 0 || amount > inv.Paid)
        {
            return Task.FromResult(new RefundResult(false, "مبلغ عودت باید بین ۱ ریال و کل مبلغ پرداخت‌شده باشد.", MapDetail(inv)));
        }

        inv.Paid = Math.Max(0, inv.Paid - amount);
        inv.Payments.Add(new PaymentRecord(PaymentMethod.Cash, "عودت وجه", NowStamp(), -amount));
        inv.State = InvoiceState.Refunded;
        Calc(inv);

        return Task.FromResult(new RefundResult(true, null, MapDetail(inv)));
    }

    // ── Invoice viewer — line editing + soft delete ────────────────────────

    public Task<InvoiceDetail?> EditLineAsync(string invoiceId, int lineIndex, int quantity, int discountPercent)
    {
        var inv = Find(invoiceId);
        if (inv is null || !IsEditable(inv) || lineIndex < 0 || lineIndex >= inv.Lines.Count)
        {
            return Task.FromResult<InvoiceDetail?>(null);
        }

        var before = inv.Lines[lineIndex];
        var nextQty = Math.Max(1, quantity);
        var nextDisc = Math.Clamp(discountPercent, 0, 100);

        if (before.Quantity == nextQty && before.DiscountPercent == nextDisc)
        {
            return Task.FromResult<InvoiceDetail?>(MapDetail(inv));
        }

        inv.Lines[lineIndex] = before with { Quantity = nextQty, DiscountPercent = nextDisc };

        var changes = new List<string>(2);
        if (before.Quantity != nextQty)
        {
            changes.Add($"تعداد {before.Quantity} → {nextQty}");
        }

        if (before.DiscountPercent != nextDisc)
        {
            changes.Add($"تخفیف {before.DiscountPercent}٪ → {nextDisc}٪");
        }

        Log(inv, InvoiceAuditKind.ItemEdited, "ویرایش ردیف",
            $"{before.Title} — {string.Join("، ", changes)}");

        Calc(inv);
        MarkHistoryDirty();
        return Task.FromResult<InvoiceDetail?>(MapDetail(inv));
    }

    public Task<InvoiceDetail?> RemoveLineAsync(string invoiceId, int lineIndex)
    {
        var inv = Find(invoiceId);
        if (inv is null || !IsEditable(inv) || lineIndex < 0 || lineIndex >= inv.Lines.Count)
        {
            return Task.FromResult<InvoiceDetail?>(null);
        }

        var removed = inv.Lines[lineIndex];
        inv.Lines.RemoveAt(lineIndex);
        Log(inv, InvoiceAuditKind.ItemRemoved, "حذف ردیف", removed.Title);

        Calc(inv);
        MarkHistoryDirty();
        return Task.FromResult<InvoiceDetail?>(MapDetail(inv));
    }

    public Task<InvoiceDetail?> DeleteInvoiceAsync(string invoiceId)
    {
        var inv = Find(invoiceId);
        if (inv is null || inv.State == InvoiceState.Deleted)
        {
            return Task.FromResult<InvoiceDetail?>(null);
        }

        inv.State = InvoiceState.Deleted;
        Log(inv, InvoiceAuditKind.Deleted, "حذف فاکتور",
            $"فاکتور {inv.Id} حذف شد" + (inv.Paid > 0 ? $" (پرداخت‌شده: {inv.Paid:N0})" : string.Empty));

        MarkHistoryDirty();
        return Task.FromResult<InvoiceDetail?>(MapDetail(inv));
    }

    private static bool IsEditable(InvoiceRecord inv) =>
        inv.State is InvoiceState.Draft or InvoiceState.Partial;

    public Task<IReadOnlyList<OwnerRef>> GetOwnersAsync(string query)
    {
        var q = PersianText.Normalize(query);
        IReadOnlyList<OwnerRef> list = Owners
            .Where(o => q.Length == 0 || PersianText.Contains(
                PersianText.Normalize($"{o.FirstName} {o.LastName} {o.FileCode} {o.Mobile} {o.PetName}"), q))
            .Take(20)
            .ToList();

        return Task.FromResult(list);
    }

    public Task<InvoiceDetail> PickOwnerAsync(string invoiceId, OwnerPickResult pick)
    {
        var inv = Find(invoiceId) ?? throw new InvalidOperationException($"Unknown invoice: {invoiceId}");
        inv.OwnerName = pick.IsGuest ? string.Empty : pick.OwnerName;
        inv.PetName = pick.IsGuest ? string.Empty : pick.PetName;
        inv.Phone = pick.IsGuest ? string.Empty : pick.Phone;

        MarkHistoryDirty();
        return Task.FromResult(MapDetail(inv));
    }

    // ── Cashier history — the «تاریخچه صندوق» ledger ───────────────────────

    /// <summary>
    /// The cached ledger (rebuilt only after a mutation, PERFORMANCE.md §13).
    /// </summary>
    private IReadOnlyList<LedgerRow> EnsureLedger()
    {
        if (_historyDirty || _ledgerRows is null)
        {
            _ledgerRows = BuildLedger();
            _historyDirty = false;
        }

        return _ledgerRows;
    }

    private void MarkHistoryDirty() => _historyDirty = true;

    public Task<CashierHistoryPage> GetHistoryAsync(CashierHistoryFilter filter)
    {
        var rows = EnsureLedger();
        var kinds = filter.Kinds ?? [];

        var from = string.IsNullOrWhiteSpace(filter.FromDate) ? null : filter.FromDate;
        var to = string.IsNullOrWhiteSpace(filter.ToDate) ? null : filter.ToDate;
        var q = PersianText.Normalize(filter.Search);

        // Filter (over the cached ledger) → sort → page. The total + inflow/
        // outflow sums are computed over the FULL filtered set, so the summary
        // bar stays accurate while only a single page is materialised on the UI
        // thread (PERFORMANCE.md §6). Ordinal compares are correct because the
        // Jalali dates are fixed-width "yyyy/MM/dd" strings.
        var matched = new List<LedgerRow>(rows.Count);
        long inflow = 0, outflow = 0;
        foreach (var row in rows)
        {
            if (from is not null && string.CompareOrdinal(row.Date, from) < 0)
            {
                continue;
            }

            if (to is not null && string.CompareOrdinal(row.Date, to) > 0)
            {
                continue;
            }

            if (kinds.Count > 0 && !kinds.Contains(row.Kind))
            {
                continue;
            }

            if (q.Length > 0 && !PersianText.Contains(row.Haystack, q))
            {
                continue;
            }

            matched.Add(row);
            if (row.AmountSign > 0)
            {
                inflow += row.Amount;
            }
            else if (row.AmountSign < 0)
            {
                outflow += row.Amount;
            }
        }

        matched.Sort(filter.Sort switch
        {
            CashierHistorySort.Oldest => OldestComparer,
            CashierHistorySort.AmountDesc => AmountDescComparer,
            CashierHistorySort.AmountAsc => AmountAscComparer,
            CashierHistorySort.Owner => OwnerComparer,
            _ => NewestComparer,
        });

        var pageSize = Math.Max(1, filter.PageSize);
        var skip = (Math.Max(1, filter.Page) - 1) * pageSize;
        var take = Math.Min(pageSize, Math.Max(0, matched.Count - skip));

        IReadOnlyList<CashierHistoryEntry> items = take <= 0
            ? []
            : matched.GetRange(skip, take).Select(MapEntry).ToList();

        return Task.FromResult(new CashierHistoryPage(items, matched.Count, inflow, outflow));
    }

    /// <summary>
    /// Flattens every invoice into the ledger's rows: one «پذیرش» per invoice,
    /// one row per payment (پرداخت / تسویه / عودت by state + sign) and a terminal
    /// «کنسل/عودت» row for void/refunded invoices that carry no payment rows.
    /// The per-row search haystack is normalized once here, never per keystroke
    /// (PERFORMANCE.md §2/§9).
    /// </summary>
    private IReadOnlyList<LedgerRow> BuildLedger()
    {
        var rows = new List<LedgerRow>();

        foreach (var inv in _invoices)
        {
            if (inv.Date.Length == 0)
            {
                continue; // un-picked-up drafts carry no date yet — not ledger events
            }

            rows.Add(MapRow(
                $"acc-{inv.Id}", CashierHistoryKind.Accepted, inv.State, inv,
                inv.Date, "", 0, 0, "پذیرش فاکتور در صندوق"));

            for (var p = 0; p < inv.Payments.Count; p++)
            {
                var pay = inv.Payments[p];
                var isRefund = pay.Amount < 0;
                var kind = isRefund
                    ? CashierHistoryKind.Refunded
                    : inv.State == InvoiceState.Settled ? CashierHistoryKind.Settled : CashierHistoryKind.Paid;
                var note = isRefund
                    ? "عودت وجه به مشتری"
                    : pay.Note.Length > 0 ? pay.Note : "ثبت پرداخت";

                rows.Add(MapRow(
                    $"pay-{inv.Id}-{p}", kind, inv.State, inv,
                    inv.Date, pay.At, Math.Abs(pay.Amount), Math.Sign(pay.Amount), note));
            }

            if (inv.Payments.Count == 0 && inv.State is InvoiceState.Void or InvoiceState.Refunded)
            {
                var isVoid = inv.State == InvoiceState.Void;
                rows.Add(MapRow(
                    $"term-{inv.Id}",
                    isVoid ? CashierHistoryKind.Cancelled : CashierHistoryKind.Refunded,
                    inv.State, inv, inv.Date, "", 0, 0,
                    isVoid ? "ابطال فاکتور" : "عودت فاکتور"));
            }

            // Fold the invoice's non-payment audit trail into the ledger so line
            // edits, discount/insurance changes and soft deletes are all recorded
            // in the history with their detail. «ایجاد» is skipped (already the
            // «پذیرش» row above) and «ابطال» is skipped (already the terminal row).
            for (var a = 0; a < inv.Audit.Count; a++)
            {
                var entry = inv.Audit[a];
                var kind = entry.Kind switch
                {
                    InvoiceAuditKind.Deleted => CashierHistoryKind.Deleted,
                    InvoiceAuditKind.Created or InvoiceAuditKind.Voided => (CashierHistoryKind?)null,
                    _ => CashierHistoryKind.Edited,
                };

                if (kind is null)
                {
                    continue;
                }

                rows.Add(MapRow(
                    $"aud-{inv.Id}-{a}", kind.Value, inv.State, inv,
                    inv.Date, entry.At, 0, 0, entry.Action + " — " + entry.Detail));
            }
        }

        return rows;
    }

    private static LedgerRow MapRow(
        string id, CashierHistoryKind kind, InvoiceState state,
        InvoiceRecord inv, string date, string time, long amount, int sign, string note)
    {
        var owner = string.IsNullOrEmpty(inv.OwnerName) ? "مهمان" : inv.OwnerName;
        return new LedgerRow(id, kind, state, inv.Id, owner, inv.PetName, date, time, amount, sign, note)
        {
            Haystack = PersianText.Normalize($"{inv.Id} {owner} {inv.PetName} {note}"),
        };
    }

    private static CashierHistoryEntry MapEntry(LedgerRow row) => new(
        row.Id, row.Kind, KindLabel(row.Kind), row.InvoiceId, row.OwnerName, row.PetName,
        row.Date, row.Time, row.Amount, row.AmountSign, row.State, StateLabel(row.State), row.Note);

    private static int CompareDate(LedgerRow a, LedgerRow b, bool ascending)
    {
        var byDate = string.CompareOrdinal(a.Date, b.Date);
        if (byDate == 0)
        {
            byDate = string.CompareOrdinal(a.Time, b.Time);
        }

        if (byDate == 0)
        {
            byDate = string.CompareOrdinal(a.Id, b.Id);
        }

        return ascending ? byDate : -byDate;
    }

    // Static comparers (allocated once, not per query — PERFORMANCE.md §2).
    private static readonly Comparison<LedgerRow> NewestComparer = static (a, b) => CompareDate(a, b, ascending: false);
    private static readonly Comparison<LedgerRow> OldestComparer = static (a, b) => CompareDate(a, b, ascending: true);
    private static readonly Comparison<LedgerRow> AmountDescComparer = static (a, b) => b.Amount.CompareTo(a.Amount);
    private static readonly Comparison<LedgerRow> AmountAscComparer = static (a, b) => a.Amount.CompareTo(b.Amount);
    private static readonly Comparison<LedgerRow> OwnerComparer = static (a, b) =>
    {
        var byName = string.Compare(a.OwnerName, b.OwnerName, StringComparison.CurrentCulture);
        return byName != 0 ? byName : CompareDate(a, b, ascending: false);
    };

    private static string KindLabel(CashierHistoryKind kind) => kind switch
    {
        CashierHistoryKind.Accepted => "پذیرش",
        CashierHistoryKind.Paid => "پرداخت",
        CashierHistoryKind.Settled => "تسویه",
        CashierHistoryKind.Cancelled => "کنسل",
        CashierHistoryKind.Refunded => "عودت",
        CashierHistoryKind.Edited => "ویرایش",
        CashierHistoryKind.Deleted => "حذف",
        _ => "رویداد",
    };

    private static string StateLabel(InvoiceState state) => state switch
    {
        InvoiceState.Draft => "پیش‌نویس",
        InvoiceState.Partial => "تسویه جزئی",
        InvoiceState.Settled => "تسویه کامل",
        InvoiceState.Void => "ابطال شده",
        InvoiceState.Refunded => "مسترد شده",
        InvoiceState.Deleted => "حذف شده",
        _ => "—",
    };

    private void PushRecent(string kind, string refId)
    {
        _recent.RemoveAll(r => r.Kind == kind && r.Ref == refId);
        _recent.Insert(0, new RecentItemRef(kind, refId));
        while (_recent.Count > 6)
        {
            _recent.RemoveAt(_recent.Count - 1);
        }
    }

    private static IReadOnlyList<CatalogFilterRef> BuildFilters()
    {
        var svcCt = Catalog.Count(x => x.Kind == "svc");
        var itemCt = Catalog.Count(x => x.Kind == "item");

        var filters = new List<CatalogFilterRef>
        {
            new("all", "همه", Catalog.Length),
            new("svc", "خدمات", svcCt),
            new("item", "دارو و کالا", itemCt),
        };

        var groups = Catalog
            .GroupBy(x => (x.GroupKey, x.GroupLabel))
            .Select(g => new CatalogFilterRef(g.Key.GroupKey, g.Key.GroupLabel, g.Count()))
            .OrderByDescending(g => g.Count);

        filters.AddRange(groups);
        return filters;
    }

    private static string NowStamp() =>
        PersianNumberFormatter.ToPersianDigits($"{DateTime.Now.Hour:D2}:{DateTime.Now.Minute:D2}");

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
        MarkHistoryDirty();
        return Task.FromResult(MapDetail(record));
    }

    public Task<InvoiceDetail> CreateBlankAsync(InvoiceKind kind)
    {
        var record = NewRecord(
            $"INV-{_invoiceCounter++:D3}", kind, null, "", "", "",
            [], DiscountKind.Percent, 0, null, InvoiceState.Draft, 0, []);

        _invoices.Insert(0, record);
        RecalcAll();
        MarkHistoryDirty();
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
        var record = new InvoiceRecord
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

        Log(record, InvoiceAuditKind.Created, "ایجاد فاکتور",
            $"{lines.Count} ردیف — {SideLabel(kind)}");
        return record;
    }

    private static void Log(InvoiceRecord inv, InvoiceAuditKind kind, string action, string detail) =>
        inv.Audit.Add(new AuditRecord(kind, NowStamp(), action, detail));

    private static string SideLabel(InvoiceKind kind) => kind == InvoiceKind.Visit ? "از ویزیت" : "فروش";

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
        var isLocked = inv.State is InvoiceState.Settled or InvoiceState.Void or InvoiceState.Refunded or InvoiceState.Deleted;
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

        var audit = inv.Audit
            .Select(a => new InvoiceAuditEntry(a.Kind, a.At, a.Action, a.Detail))
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
            inv.InsuranceShare > 0, inv.DiscountAmount > 0,
            audit);
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

    private sealed record AuditRecord(
        InvoiceAuditKind Kind, string At, string Action, string Detail);

    /// <summary>
    /// One flattened cashier-history row held in the cached ledger. Carries the
    /// pre-normalized <see cref="Haystack"/> so the per-query text filter never
    /// rebuilds a string per row (PERFORMANCE.md §2/§9).
    /// </summary>
    private sealed record LedgerRow(
        string Id,
        CashierHistoryKind Kind,
        InvoiceState State,
        string InvoiceId,
        string OwnerName,
        string PetName,
        string Date,
        string Time,
        long Amount,
        int AmountSign,
        string Note)
    {
        public string Haystack { get; init; } = string.Empty;
    }

    private sealed class InvoiceRecord
    {
        public required string Id { get; init; }

        public required InvoiceKind Kind { get; init; }

        /// <summary>ASCII Jalali <c>yyyy/MM/dd</c> issue date (drives the cashier-history ledger).</summary>
        public string Date { get; set; } = JalaliCalendar.Today;

        public string? EncounterId { get; init; }

        public required string OwnerName { get; set; }

        public required string PetName { get; set; }

        public required string Phone { get; set; }

        public required List<LineRecord> Lines { get; init; }

        public required DiscountKind DiscountKind { get; init; }

        public required int DiscountValue { get; init; }

        public string? InsuranceOrgId { get; init; }

        public required InvoiceState State { get; set; }

        public required long Paid { get; set; }

        public required List<PaymentRecord> Payments { get; init; }

        /// <summary>
        /// The invoice's non-payment audit trail (create / line edits / discount /
        /// insurance / void / delete), shown in the viewer's «تاریخچه تغییرات» and
        /// folded into the cashier ledger so edits and deletes are recorded with
        /// their full detail.
        /// </summary>
        public List<AuditRecord> Audit { get; } = [];

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
