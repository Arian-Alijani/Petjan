using System.Globalization;
using System.Text.Json;
using Animora.Application.UseCases.Sales.ReadModels;
using Animora.Client.Services.Inventory;
using Animora.Contracts.Identity;
using Animora.Contracts.Sales;
using Animora.Domain.Sales;
using ContractAuditKind = Animora.Contracts.Sales.InvoiceAuditKind;
using ContractDiscountKind = Animora.Contracts.Sales.DiscountKind;
using ContractInvoiceKind = Animora.Contracts.Sales.InvoiceKind;
using ContractInvoiceState = Animora.Contracts.Sales.InvoiceState;
using ContractInvoiceLine = Animora.Contracts.Sales.InvoiceLine;
using ContractLineKind = Animora.Contracts.Sales.InvoiceLineKind;
using ContractPaymentMethod = Animora.Contracts.Sales.PaymentMethod;
using DomainAdjustmentKind = Animora.Domain.Sales.AdjustmentKind;
using DomainDiscountKind = Animora.Domain.Sales.DiscountKind;
using DomainInvoiceKind = Animora.Domain.Sales.InvoiceKind;
using DomainInvoiceState = Animora.Domain.Sales.InvoiceState;
using DomainLineKind = Animora.Domain.Sales.InvoiceLineKind;
using DomainPaymentMethod = Animora.Domain.Sales.PaymentMethod;
using DomainSettlementStatus = Animora.Domain.Sales.SettlementStatus;

namespace Animora.Client.Services.Sales;

/// <summary>
/// Read-model ↔ Contracts-Sales mapping (C07). <c>Contracts</c> carries display-shaped values
/// (Jalali date/time strings, tone keys, pre-formatted Persian figures, Persian status labels) that the
/// local store does not persist, so the derivations live here — never in the ViewModel and never in SQL.
/// Every enum is mapped explicitly so the deep <see cref="Animora.Domain.Sales.Invoice"/> write model
/// (six real states, four kinds) lands on the UI's five-state vocabulary without a silent cast.
/// </summary>
internal static class SalesMapping
{
    private static readonly string[] ToneAlphabet = ["a", "b", "c", "d", "e", "f"];

    // ── Invoice kind / state ────────────────────────────────────────────────

    /// <summary>
    /// The UI kind. Boarding invoices and goods-return documents are both visit-sourced from the POS
    /// viewpoint (the reference only distinguishes «از ویزیت» vs «فروش»).
    /// </summary>
    internal static ContractInvoiceKind KindOf(DomainInvoiceKind kind) => kind switch
    {
        DomainInvoiceKind.DirectSale or DomainInvoiceKind.SalesReturn => ContractInvoiceKind.Sale,
        _ => ContractInvoiceKind.Visit,
    };

    /// <summary>
    /// The UI lifecycle state. A <see cref="DomainInvoiceState.Posted"/> invoice that carries no payment
    /// yet is still an open draft from the cashier's viewpoint; the paid/void/refunded terminals map 1:1.
    /// </summary>
    internal static ContractInvoiceState StateOf(DomainInvoiceState state) => state switch
    {
        DomainInvoiceState.Draft or DomainInvoiceState.Posted => ContractInvoiceState.Draft,
        DomainInvoiceState.PartiallyPaid => ContractInvoiceState.Partial,
        DomainInvoiceState.Paid => ContractInvoiceState.Settled,
        DomainInvoiceState.Void => ContractInvoiceState.Void,
        DomainInvoiceState.Refunded => ContractInvoiceState.Refunded,
        _ => ContractInvoiceState.Draft,
    };

    internal static string SourceLabel(DomainInvoiceKind kind) => kind switch
    {
        DomainInvoiceKind.Visit => "از ویزیت",
        DomainInvoiceKind.Boarding => "از بستری",
        _ => "فروش",
    };

    // ── Sidebar / queue ─────────────────────────────────────────────────────

    internal static InvoiceSummary ToContract(this InvoiceSummaryReadModel row, TimeZoneInfo? tz = null) =>
        new(row.Id, KindOf(row.Kind), StateOf(row.State), row.OwnerName, row.PetName, row.Phone,
            row.TotalMinor, row.DueMinor, ToneOf(row.OwnerId ?? row.Id), FirstChar(row.OwnerName),
            row.Number,
            tz is null ? row.IssuedUtc.UtcDateTime : TimeZoneInfo.ConvertTime(row.IssuedUtc, tz).DateTime);

    internal static ReadyEncounterRef ToContract(this ReadyForBillingReadModel row, TimeZoneInfo tz)
    {
        var (count, total) = HandoffPreview(row.LinesJson);
        return new ReadyEncounterRef(row.Id, row.Id, row.OwnerName, row.PetName, string.Empty,
            string.Empty, row.DoctorId, HhMm(row.CompletedUtc, tz), count, total,
            ToneOf(row.OwnerId), FirstChar(row.OwnerName));
    }

    // ── Invoice detail ──────────────────────────────────────────────────────

    internal static InvoiceDetail ToContract(this InvoiceDetailReadModel row, TimeZoneInfo tz)
    {
        var summary = row.Summary;
        var state = StateOf(summary.State);
        var isGuest = string.IsNullOrEmpty(summary.OwnerId);

        var lines = new ContractInvoiceLine[row.Lines.Count];
        for (var i = 0; i < row.Lines.Count; i++) lines[i] = row.Lines[i].ToContract(i);

        var payments = new InvoicePayment[row.Payments.Count];
        for (var i = 0; i < row.Payments.Count; i++) payments[i] = row.Payments[i].ToContract(tz);

        var insured = !string.IsNullOrEmpty(row.InsurerId);
        return new InvoiceDetail(
            summary.Id,
            summary.State == DomainInvoiceState.Draft ? string.Empty : summary.Number,
            KindOf(summary.Kind), state, StateLabel(summary.State, row),
            SourceLabel(summary.Kind), summary.OwnerName, summary.PetName, summary.Phone,
            isGuest, summary.State != DomainInvoiceState.Draft,
            ToneOf(summary.OwnerId ?? summary.Id), FirstChar(summary.OwnerName),
            summary.Kind == DomainInvoiceKind.Visit, string.Empty, string.Empty,
            row.InsurerId ?? string.Empty,
            insured ? $"{row.InsurancePercent.ToString("0.#", CultureInfo.InvariantCulture)}٪ پوشش" : "بدون بیمه",
            lines, DiscountKindOf(row.DiscountKind), (int)Math.Round(row.DiscountValue),
            payments, row.SubtotalMinor, row.DiscountMinor, EffectiveTaxPercent(row),
            row.TaxMinor, row.InsuranceMinor, summary.TotalMinor, row.PaidMinor, summary.DueMinor,
            insured, row.DiscountMinor > 0, BuildAudit(row, tz), summary.OwnerId ?? string.Empty);
    }

    private static ContractInvoiceLine ToContract(this InvoiceLineReadModel row, int index)
    {
        var isCustom = string.IsNullOrEmpty(row.CatalogItemId)
            && row.Kind is DomainLineKind.Custom or DomainLineKind.Boarding;
        var kind = row.Kind == DomainLineKind.Product ? ContractLineKind.Item : ContractLineKind.Service;
        return new ContractInvoiceLine(index, row.Title, kind, row.UnitPriceMinor,
            (int)Math.Round(row.Quantity), (int)Math.Round(row.DiscountPercent), isCustom,
            row.CatalogItemId ?? string.Empty, IconFor(row.Kind, isCustom),
            kind == ContractLineKind.Item ? "کالا" : "خدمت", row.GrossMinor, row.SubtotalMinor, row.LineId);
    }

    private static InvoicePayment ToContract(this InvoicePaymentReadModel row, TimeZoneInfo tz) =>
        new(ContractPaymentMethodOf(row.Method), MethodLabel(row.Method), row.Note ?? string.Empty,
            HhMm(row.ReceivedUtc, tz), row.AmountMinor);

    internal static ContractDiscountKind DiscountKindOf(DomainDiscountKind kind) =>
        kind == DomainDiscountKind.Amount ? ContractDiscountKind.Amount : ContractDiscountKind.Percent;

    /// <summary>
    /// The «تاریخچه تغییرات» list: the creation entry, each compensating adjustment, each money-moving
    /// payment and the terminal void/delete — ordered by real time. The produced invoice never changes
    /// (A-RULE 002); every post-posting change is an append-only adjustment carried here.
    /// </summary>
    private static IReadOnlyList<InvoiceAuditEntry> BuildAudit(InvoiceDetailReadModel row, TimeZoneInfo tz)
    {
        var entries = new List<(DateTimeOffset At, InvoiceAuditEntry Entry)>
        {
            (row.Summary.IssuedUtc, new InvoiceAuditEntry(ContractAuditKind.Created,
                HhMm(row.Summary.IssuedUtc, tz), "ایجاد فاکتور",
                $"{PersianFormat.Count(row.Lines.Count)} ردیف — {SourceLabel(row.Summary.Kind)}", ActorRef.System)),
        };

        foreach (var adjustment in row.Adjustments)
        {
            // A refund adjustment is paired with a negative payment row, which is the money-moving entry;
            // emitting it here would double the «عودت» line.
            if (adjustment.Kind == DomainAdjustmentKind.Refund)
            {
                continue;
            }
            entries.Add((adjustment.CreatedUtc, new InvoiceAuditEntry(AdjustmentKindOf(adjustment.Kind),
                HhMm(adjustment.CreatedUtc, tz), AdjustmentAction(adjustment.Kind),
                PersianFormat.Toman(adjustment.AmountMinor), ActorRef.System)));
        }

        foreach (var payment in row.Payments)
        {
            var refund = payment.AmountMinor < 0;
            entries.Add((payment.ReceivedUtc, new InvoiceAuditEntry(
                refund ? ContractAuditKind.Refunded : ContractAuditKind.PaymentTaken,
                HhMm(payment.ReceivedUtc, tz), refund ? "عودت وجه" : "ثبت پرداخت",
                $"{MethodLabel(payment.Method)} — {PersianFormat.Toman(Math.Abs(payment.AmountMinor))}",
                ActorRef.System)));
        }

        if (row.Summary.State == DomainInvoiceState.Void
            && !row.Adjustments.Any(a => a.Kind == DomainAdjustmentKind.CreditNote))
        {
            entries.Add((row.Summary.IssuedUtc, new InvoiceAuditEntry(ContractAuditKind.Voided,
                HhMm(row.Summary.IssuedUtc, tz), "ابطال فاکتور", "فاکتور باطل شد", ActorRef.System)));
        }

        entries.Sort(static (a, b) => a.At.CompareTo(b.At));
        var result = new InvoiceAuditEntry[entries.Count];
        for (var i = 0; i < entries.Count; i++) result[i] = entries[i].Entry;
        return result;
    }

    private static ContractAuditKind AdjustmentKindOf(DomainAdjustmentKind kind) => kind switch
    {
        DomainAdjustmentKind.LineReturn => ContractAuditKind.LineReturned,
        DomainAdjustmentKind.CreditNote => ContractAuditKind.Voided,
        _ => ContractAuditKind.DiscountChanged,
    };

    private static string AdjustmentAction(DomainAdjustmentKind kind) => kind switch
    {
        DomainAdjustmentKind.LineReturn => "برگشت کالا",
        DomainAdjustmentKind.CreditNote => "ابطال و اصلاح",
        DomainAdjustmentKind.WriteOff => "تعدیل مطالبات",
        DomainAdjustmentKind.RoundingCorrection => "اصلاح رُند",
        _ => "اصلاح",
    };

    // ── Catalog / insurers / owners ─────────────────────────────────────────

    internal static CatalogSnapshot ToContract(this SalesCatalogReadModel row)
    {
        var items = new CatalogItem[row.Items.Count];
        for (var i = 0; i < row.Items.Count; i++) items[i] = row.Items[i].ToContract();

        var byKey = new Dictionary<string, (string Label, int Count)>(StringComparer.Ordinal)
        {
            ["all"] = ("همه", items.Length),
        };
        var svc = 0;
        var itemCt = 0;
        foreach (var item in row.Items)
        {
            if (item.Kind == "Service")
            {
                // Services are represented solely by the dedicated «خدمات» chip below. They must not
                // also seed a per-category chip, otherwise the service category (SRV → «خدمات») would
                // surface a second, duplicate «خدمات» tab alongside it.
                svc++;
                continue;
            }

            itemCt++;
            var key = Slug(item.Category);
            var current = byKey.TryGetValue(key, out var existing) ? existing.Count : 0;
            byKey[key] = (item.Category, current + 1);
        }
        var filters = new List<CatalogFilterRef>
        {
            new("all", "همه", items.Length),
            new("svc", "خدمات", svc),
            new("item", "دارو و کالا", itemCt),
        };
        foreach (var pair in byKey)
        {
            if (pair.Key is "all" or "svc" or "item") continue;
            filters.Add(new CatalogFilterRef(pair.Key, pair.Value.Label, pair.Value.Count));
        }

        var recent = new RecentItemRef[row.RecentIds.Count];
        for (var i = 0; i < row.RecentIds.Count; i++)
        {
            var id = row.RecentIds[i];
            recent[i] = new RecentItemRef(KindOfCatalogId(items, id), id);
        }
        return new CatalogSnapshot(items, filters, recent);
    }

    internal static CatalogItem ToContract(this SalesCatalogItemReadModel row) =>
        new(row.Id, row.Kind == "Service" ? "svc" : "item", row.Title, row.Category, Slug(row.Category),
            row.PriceMinor, row.Kind == "Service" ? "IconClipboard" : "IconBox");

    internal static InsurerRef ToContract(this InsurerReadModel row) =>
        new(row.Id, row.Name, (int)Math.Round(row.CoveragePercent));

    internal static OwnerRef ToContract(this OwnerLookupReadModel row)
    {
        var (first, last) = SplitName(row.Name);
        return new OwnerRef(row.Id, first, last, string.Empty, row.Phone, ToneOf(row.Id), string.Empty);
    }

    // ── Cashier history ─────────────────────────────────────────────────────

    internal static CashierHistoryEntry ToContract(this CashierHistoryReadModel row, TimeZoneInfo tz)
    {
        var kind = HistoryKindOf(row.Kind);
        var state = StateOf(row.InvoiceState);
        return new CashierHistoryEntry(row.Id, kind, HistoryKindLabel(kind), row.InvoiceId, row.OwnerName,
            string.Empty, Jalali(row.OccurredUtc, tz), HhMm(row.OccurredUtc, tz),
            Math.Abs(row.AmountMinor), Math.Sign(row.AmountMinor), state, StateText(state), string.Empty,
            ActorRef.System);
    }

    internal static CashierHistoryKind HistoryKindOf(string kind) => kind switch
    {
        "Payment" => CashierHistoryKind.Paid,
        "Refund" => CashierHistoryKind.Refunded,
        "Void" => CashierHistoryKind.Cancelled,
        _ => CashierHistoryKind.Accepted,
    };

    private static string HistoryKindLabel(CashierHistoryKind kind) => kind switch
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

    /// <summary>The Contracts-history label used on the wire for one selected ledger kind (see <c>ISalesQueries</c>).</summary>
    internal static string? HistoryFilterKind(CashierHistoryKind kind) => kind switch
    {
        CashierHistoryKind.Paid or CashierHistoryKind.Settled => "Payment",
        CashierHistoryKind.Refunded => "Refund",
        CashierHistoryKind.Cancelled => "Void",
        CashierHistoryKind.Accepted => "Accepted",
        // «ویرایش» / «حذف» are audit-trail events the cashier ledger never projects, so they have no wire kind.
        _ => null,
    };

    /// <summary>
    /// The wire kind filter for a multi-select: distinct projected kinds joined by commas, or <c>null</c>
    /// (no restriction) when nothing is selected or the selection already covers every projected kind.
    /// </summary>
    internal static string? HistoryFilterKinds(IReadOnlyCollection<CashierHistoryKind> kinds)
    {
        if (kinds.Count == 0) return null;
        var wire = kinds.Select(HistoryFilterKind).Where(k => k is not null).Distinct(StringComparer.Ordinal).ToArray();
        return wire.Length is 0 or >= 4 ? null : string.Join(',', wire);
    }

    internal static string StateText(ContractInvoiceState state) => state switch
    {
        ContractInvoiceState.Draft => "پیش‌نویس",
        ContractInvoiceState.Partial => "تسویه جزئی",
        ContractInvoiceState.Settled => "تسویه کامل",
        ContractInvoiceState.Void => "ابطال شده",
        ContractInvoiceState.Refunded => "مسترد شده",
        ContractInvoiceState.Deleted => "حذف شده",
        _ => "—",
    };

    // ── Shared derivations ──────────────────────────────────────────────────

    /// <summary>
    /// The state's Persian chip label, with the settlement verdict folded in (R-B16): a failure or a
    /// pending broadcast is never hidden (ARCHITECTURE §8.3). The Contracts shape has no dedicated
    /// settlement field, so the chip — the field the UI already renders — carries it.
    /// </summary>
    internal static string StateLabel(DomainInvoiceState state, InvoiceDetailReadModel row)
    {
        var label = StateText(StateOf(state));
        var suffix = SettlementSuffix(row.SettlementStatus, row.LastSettlementErrorCode, row.ResponsibleRole);
        return suffix.Length == 0 ? label : label + suffix;
    }

    private static string SettlementSuffix(DomainSettlementStatus status, string? error, string? role) =>
        status switch
        {
            // A provisional post may carry a warning (D07: oversold under the permissive offline policy).
            DomainSettlementStatus.LocallyPosted => " · ارسال محلی" + Detail(error, null),
            DomainSettlementStatus.PendingSettlement => " · در انتظار سرور" + Detail(error, null),
            DomainSettlementStatus.Settled => " · تسویه سرور",
            DomainSettlementStatus.Reversed => " · معکوس شده",
            DomainSettlementStatus.Rejected => " · رد سرور" + Detail(error, role),
            DomainSettlementStatus.Conflict => " · تعارض" + Detail(error, role),
            DomainSettlementStatus.NeedsAttention => " · نیازمند بررسی" + Detail(error, role),
            _ => string.Empty,
        };

    private static string Detail(string? error, string? role)
    {
        var inner = string.Join(" / ", new[] { error, role }.Where(s => !string.IsNullOrWhiteSpace(s)));
        return inner.Length == 0 ? string.Empty : $" ({inner})";
    }

    /// <summary>
    /// The effective invoice tax rate for display: the tax the domain computed over the taxable
    /// after-discount base. Zero when nothing is taxable.
    /// </summary>
    private static int EffectiveTaxPercent(InvoiceDetailReadModel row)
    {
        var taxable = row.SubtotalMinor - row.DiscountMinor;
        return taxable <= 0 || row.TaxMinor <= 0 ? 0 : (int)Math.Round(row.TaxMinor * 100.0 / taxable);
    }

    private static ContractPaymentMethod ContractPaymentMethodOf(DomainPaymentMethod method) => method switch
    {
        DomainPaymentMethod.Cash => ContractPaymentMethod.Cash,
        DomainPaymentMethod.Card or DomainPaymentMethod.Transfer => ContractPaymentMethod.Card,
        DomainPaymentMethod.Cheque => ContractPaymentMethod.Cheque,
        DomainPaymentMethod.Credit or DomainPaymentMethod.Insurance => ContractPaymentMethod.Credit,
        _ => ContractPaymentMethod.Cash,
    };

    internal static string MethodLabel(DomainPaymentMethod method) => method switch
    {
        DomainPaymentMethod.Cash or DomainPaymentMethod.Wallet => "نقدی",
        DomainPaymentMethod.Card or DomainPaymentMethod.Transfer => "کارت",
        DomainPaymentMethod.Cheque => "چک",
        DomainPaymentMethod.Credit or DomainPaymentMethod.Insurance => "نسیه",
        _ => "—",
    };

    private static string IconFor(DomainLineKind kind, bool isCustom)
    {
        if (isCustom) return "IconEdit";
        return kind == DomainLineKind.Product ? "IconBox" : "IconClipboard";
    }

    private static string KindOfCatalogId(IReadOnlyList<CatalogItem> items, string id)
    {
        for (var i = 0; i < items.Count; i++)
        {
            if (string.Equals(items[i].Id, id, StringComparison.Ordinal)) return items[i].Kind;
        }
        return "item";
    }

    private static (string First, string Last) SplitName(string name)
    {
        var trimmed = name.Trim();
        var space = trimmed.IndexOf(' ');
        return space < 0 ? (trimmed, string.Empty) : (trimmed[..space], trimmed[(space + 1)..].Trim());
    }

    private static string FirstChar(string value) => string.IsNullOrWhiteSpace(value) ? "" : value.Trim()[..1];

    /// <summary>
    /// A stable, ASCII, lower-case filter key derived from a group label so the picker's category chips
    /// have a stable <c>key</c> independent of the rendered Persian label. Pure-ASCII labels keep a readable
    /// slug; any label containing other characters (Persian category names) gets a deterministic FNV-1a hash
    /// suffix so distinct labels never collapse into the same key. The <c>cat-</c> prefix keeps the key clear
    /// of the reserved <c>all</c>/<c>svc</c>/<c>item</c> chips. Chip keys and item group keys MUST both use it.
    /// </summary>
    private static string Slug(string label)
    {
        if (string.IsNullOrWhiteSpace(label)) return "cat-misc";
        var trimmed = label.Trim();
        var builder = new System.Text.StringBuilder(trimmed.Length + 12);
        var pure = true;
        foreach (var c in trimmed)
        {
            if (c is >= 'a' and <= 'z' || c is >= '0' and <= '9') builder.Append(c);
            else if (c is >= 'A' and <= 'Z') builder.Append((char)(c + 32));
            else if (c == ' ' || c == '-' || c == '_') builder.Append('-');
            else pure = false;
        }
        if (pure && builder.Length > 0) return "cat-" + builder;
        uint hash = 2166136261;
        foreach (var c in trimmed) hash = unchecked((hash ^ c) * 16777619);
        return builder.Length == 0 ? $"cat-{hash:x8}" : $"cat-{builder}-{hash:x8}";
    }

    /// <summary>
    /// The UI tone key (<c>a</c>…<c>f</c>). The domain has no presentation column, so the tone is a stable
    /// derivation of the row id — the same row always renders the same tint (PERFORMANCE §2).
    /// </summary>
    private static string ToneOf(string id)
    {
        var sum = 0;
        for (var i = 0; i < id.Length; i++) sum = (sum + id[i]) % 6;
        return ToneAlphabet[sum];
    }

    private static string Jalali(DateTimeOffset utc, TimeZoneInfo tz)
    {
        var local = TimeZoneInfo.ConvertTime(utc, tz).DateTime;
        var calendar = new PersianCalendar();
        return $"{calendar.GetYear(local):0000}/{calendar.GetMonth(local):00}/{calendar.GetDayOfMonth(local):00}";
    }

    private static string HhMm(DateTimeOffset utc, TimeZoneInfo tz) =>
        TimeZoneInfo.ConvertTime(utc, tz).ToString("HH:mm", CultureInfo.InvariantCulture);

    /// <summary>
    /// Reads the line count and previewed total out of a handoff's stored <c>LinesJson</c> so the
    /// «صف ویزیت» card can show them before the invoice is built. Tolerant of both the visit
    /// (<c>ServiceLines</c>) and boarding (<c>Items</c> + <c>TotalMinor</c>) shapes.
    /// </summary>
    private static (int Count, long Total) HandoffPreview(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return (0, 0);
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.TryGetProperty("TotalMinor", out var total) && total.TryGetInt64(out var totalMinor))
            {
                var count = root.TryGetProperty("Items", out var items) ? items.GetArrayLength() : 0;
                return (count, totalMinor);
            }
            if (root.TryGetProperty("ServiceLines", out var services))
            {
                long sum = 0;
                foreach (var line in services.EnumerateArray())
                {
                    var quantity = line.TryGetProperty("Quantity", out var q) && q.TryGetProperty("Value", out var v)
                        ? v.GetDecimal() : 1m;
                    var price = line.TryGetProperty("UnitPrice", out var up) && up.TryGetProperty("Minor", out var m)
                        ? m.GetInt64() : 0L;
                    sum += (long)Math.Round(price * quantity, MidpointRounding.AwayFromZero);
                }
                return (services.GetArrayLength(), sum);
            }
        }
        catch (JsonException)
        {
            // A malformed preview never blocks the queue; the card simply shows zeroes.
        }
        return (0, 0);
    }
}
