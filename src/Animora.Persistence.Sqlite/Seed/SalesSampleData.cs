using System.Globalization;
using Animora.Application.Commands;
using Animora.Contracts.Wire.Commands;
using Animora.Domain.Boarding;
using Animora.Domain.Inventory;
using Animora.Domain.Patients;
using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Domain.Sales;
using Animora.Domain.Time;
using Animora.Domain.ValueObjects;
using Animora.Persistence.Sqlite.Context;
using Animora.Persistence.Sqlite.Entities;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.Sqlite.Seed;

/// <summary>
/// Optional example sales/cashier data (C07), behind a marker and an existing-invoice check.
/// </summary>
/// <remarks>
/// Seeds <b>30 invoices</b> across the whole lifecycle — draft, posted (unpaid), partially paid, paid and
/// void — with the payments, credit-note adjustments and matching <see cref="MovementKind.Sale"/> /
/// <see cref="MovementKind.ReturnIn"/> stock movements each state implies. Every posted invoice is assigned a
/// real <c>{yy}-{seq:000000}</c> number and carries a matching <see cref="PendingCommand"/> settlement row so
/// the cashier, finance and history screens render real data (including the <c>LocallyPosted</c> chip) on a
/// fresh store. The two numbering counters (<c>invoice</c> and <c>command</c>) are advanced past the sample
/// rows, so the first live posting continues at the next value and never collides on the unique indexes.
/// Stock on-hand is never written here — only appended <see cref="StockMovement"/> rows are added, and the
/// store's save path folds them into the snapshot (C06).
/// </remarks>
public sealed class SalesSampleData(
    AnimoraLocalDbContext db,
    ILocalTenantContext tenant,
    IClock clock,
    ISeedPeriodCodeProvider periodCodes)
{
    public const string SampleKey = "sales.sample";

    private const string SeedUser = "U-SEED";
    private const decimal TaxPercent = 9m;

    /// <summary>One sample invoice: lifecycle state and its composition inputs.</summary>
    private sealed record Plan(string State, int OwnerIndex, int PetIndex, int VariantCount,
        decimal DiscountPercent, int InsurerIndex, int PaymentFraction, bool ReturnFirstLine);

    // 30 invoices: 6 draft, 7 posted, 6 partially paid, 7 paid, 4 void (24 posted -> 24 settlement rows).
    private static readonly Plan[] Plans =
    [
        new("draft", 0, 0, 2, 0m, -1, 0, false),
        new("draft", 1, 1, 1, 5m, -1, 0, false),
        new("draft", 2, 2, 3, 10m, 0, 0, false),
        new("draft", 3, 3, 2, 0m, -1, 0, false),
        new("draft", 4, 4, 1, 0m, 2, 0, false),
        new("draft", 5, 5, 4, 0m, -1, 0, false),
        new("posted", 6, 6, 2, 0m, -1, 0, false),
        new("posted", 7, 7, 3, 10m, -1, 0, false),
        new("posted", 8, 8, 1, 0m, 1, 0, false),
        new("posted", 9, 9, 2, 5m, -1, 0, false),
        new("posted", 10, 10, 3, 0m, -1, 0, false),
        new("posted", 11, 11, 2, 0m, 3, 0, false),
        new("posted", 0, 12, 1, 0m, -1, 0, false),
        new("partial", 1, 13, 2, 0m, -1, 5, false),
        new("partial", 2, 14, 3, 10m, -1, 5, false),
        new("partial", 3, 15, 1, 0m, 4, 5, false),
        new("partial", 4, 16, 2, 5m, -1, 5, false),
        new("partial", 5, 17, 3, 0m, -1, 5, false),
        new("partial", 6, 0, 2, 0m, -1, 5, false),
        new("paid", 7, 1, 2, 0m, -1, 10, false),
        new("paid", 8, 2, 3, 5m, -1, 10, false),
        new("paid", 9, 3, 1, 0m, 0, 10, true),
        new("paid", 10, 4, 2, 0m, -1, 10, false),
        new("paid", 11, 5, 3, 10m, -1, 10, false),
        new("paid", 0, 6, 2, 0m, 5, 10, true),
        new("paid", 1, 7, 1, 0m, -1, 10, false),
        new("void", 2, 8, 2, 0m, -1, 0, false),
        new("void", 3, 9, 1, 0m, -1, 0, false),
        new("void", 4, 10, 3, 0m, -1, 0, false),
        new("void", 5, 11, 2, 10m, -1, 0, false),
    ];

    public bool AlreadySeeded() => db.Set<Invoice>().IgnoreQueryFilters().Any();

    public void Populate()
    {
        var tenantId = tenant.TenantId;
        var now = clock.UtcNow;

        var owners = db.Set<Owner>().AsNoTracking().OrderBy(o => o.Id).Take(12).ToArray();
        var pets = db.Set<Pet>().AsNoTracking().OrderBy(p => p.Id).Take(18).ToArray();
        var products = db.Set<Product>().AsNoTracking().Where(p => p.IsActive && p.Kind != ProductKind.Service)
            .OrderBy(p => p.Sku).Take(20).ToArray();
        var services = db.Set<BoardingServiceDef>().AsNoTracking().Where(s => s.IsActive)
            .OrderBy(s => s.Code).Take(6).ToArray();
        var insurers = db.Set<Insurer>().AsNoTracking().OrderBy(i => i.Code).ToArray();
        if (owners.Length == 0 || pets.Length == 0 || products.Length == 0) return;

        var periods = periodCodes.For(now);
        var separator = periods.PeriodCode.IndexOf('-');
        var jalaliYear = separator < 0 ? periods.PeriodCode : periods.PeriodCode[..separator];
        if (jalaliYear.Length != 4) jalaliYear = "1400";
        var yy = jalaliYear[^2..];

        var sequence = 0L;
        var localSequence = 0L;
        for (var i = 0; i < Plans.Length; i++)
        {
            var plan = Plans[i];
            var isDraft = plan.State == "draft";
            var owner = owners[plan.OwnerIndex % owners.Length];
            var pet = pets[plan.PetIndex % pets.Length];
            var issued = now.AddDays(-(Plans.Length - i)).AddHours(i % 9);

            var created = Invoice.Create(tenantId, "draft-" + IdGenerator.NewId(issued),
                InvoiceKind.DirectSale, issued, SeedUser, owner.Id, null, pet.Id,
                operationId: IdGenerator.NewId(issued));
            if (created.IsFailure) continue;
            var invoice = created.Value!;

            AddLines(invoice, tenantId, plan, products, services, issued);

            if (plan.DiscountPercent > 0m) invoice.SetDiscount(DiscountKind.Percent, plan.DiscountPercent);
            if (plan.InsurerIndex >= 0 && insurers.Length > 0)
            {
                var insurer = insurers[plan.InsurerIndex % insurers.Length];
                invoice.SetInsurance(insurer.Id, insurer.CoveragePercent);
            }

            if (isDraft)
            {
                db.Add(invoice);
                continue;
            }

            sequence++;
            var number = string.Create(CultureInfo.InvariantCulture, $"{yy}-{sequence:000000}");
            invoice.AssignInvoiceNumber(number);
            var posted = invoice.Post(clock);
            if (posted.IsFailure) continue;

            // One Sale movement per stockable line, costed from the variant's catalogue cost.
            foreach (var line in invoice.Lines.Where(l => l.ProductId is not null && l.VariantId is not null))
            {
                var cost = CostOf(products, line.ProductId!, line.VariantId!);
                var movement = StockMovement.Create(tenantId, line.ProductId!, line.VariantId!,
                    MovementKind.Sale, new Quantity(-line.Quantity.Value, line.Quantity.UnitCode),
                    cost, invoice.PostedUtc ?? issued, SeedUser,
                    sourceEntityType: "Invoice", sourceEntityId: invoice.Id,
                    operationId: invoice.OperationId, reason: "Sale");
                if (movement.IsSuccess) db.Add(movement.Value!);
            }

            if (plan.State == "void")
            {
                localSequence++;
                db.Add(PendingFor(tenantId, invoice, localSequence, SettlementState.Reversed, now));
                invoice.Void("Sample: cancelled at the counter", clock);
                db.Add(invoice);
                continue;
            }

            if (plan.PaymentFraction > 0)
            {
                var fraction = plan.PaymentFraction / 10m;
                var amount = new Money((long)decimal.Truncate(invoice.DueTotal.Minor * fraction), invoice.Currency);
                if (amount.Minor > 0m)
                {
                    var method = i % 3 == 0 ? PaymentMethod.Card : i % 3 == 1 ? PaymentMethod.Transfer : PaymentMethod.Cash;
                    var payment = Payment.Create(tenantId, invoice.Id, method, amount,
                        (invoice.PostedUtc ?? issued).AddMinutes(30), SeedUser,
                        cashSessionId: method == PaymentMethod.Cash ? "sample-session" : null,
                        cardReference: method == PaymentMethod.Card ? $"POS-{sequence:D4}" : null,
                        operationId: IdGenerator.NewId(issued), note: "Sample receipt");
                    if (payment.IsSuccess && invoice.ApplyPayment(payment.Value!).IsSuccess)
                        db.Add(payment.Value!);
                }
            }

            if (plan.ReturnFirstLine)
            {
                var line = invoice.Lines.FirstOrDefault(l => l.ProductId is not null && l.VariantId is not null);
                if (line is not null)
                {
                    var returned = (invoice.PostedUtc ?? issued).AddDays(2);
                    var adjustment = new InvoiceAdjustment(IdGenerator.NewId(returned), AdjustmentKind.LineReturn,
                        line.LineNet, $"Return line {line.LineId}: sample customer return", returned,
                        SeedUser, IdGenerator.NewId(returned), null);
                    if (invoice.AddCreditNote(adjustment).IsSuccess)
                    {
                        var cost = CostOf(products, line.ProductId!, line.VariantId!);
                        var movement = StockMovement.Create(tenantId, line.ProductId!, line.VariantId!,
                            MovementKind.ReturnIn, line.Quantity, cost, returned, SeedUser,
                            sourceEntityType: "Invoice", sourceEntityId: invoice.Id, reason: "Customer return");
                        if (movement.IsSuccess) db.Add(movement.Value!);
                    }
                }
            }

            localSequence++;
            db.Add(PendingFor(tenantId, invoice, localSequence, SettlementState.LocallyPosted, now));
            db.Add(invoice);
        }

        // Advance both counters past the sample rows so the first live allocation continues cleanly.
        if (sequence > 0)
            db.Add(LocalSequenceCounter.Create($"invoice:{tenantId}:{jalaliYear}", sequence));
        if (localSequence > 0)
            db.Add(LocalSequenceCounter.Create(LocalSequenceCounter.DefaultName, localSequence));
    }

    private void AddLines(Invoice invoice, string tenantId, Plan plan,
        Product[] products, BoardingServiceDef[] services, DateTimeOffset issued)
    {
        for (var l = 0; l < plan.VariantCount; l++)
        {
            var product = products[(plan.OwnerIndex + l) % products.Length];
            if (product.Variants.Count == 0) continue;
            var variant = product.Variants[l % product.Variants.Count];
            var quantity = new Quantity(1 + (l % 3), product.UnitCode);
            var line = new InvoiceLine(IdGenerator.NewId(issued), InvoiceLineKind.Product,
                variant.VariantId, product.Id, variant.VariantId, product.Name, quantity,
                variant.SalePrice, 0m, TaxPercent, InvoiceLineSource.Catalog, null);
            invoice.AddLine(line);
        }

        // Every invoice also carries one service line (no stock movement).
        if (services.Length > 0)
        {
            var service = services[plan.OwnerIndex % services.Length];
            var serviceLine = new InvoiceLine(IdGenerator.NewId(issued), InvoiceLineKind.Service,
                service.Id, null, null, service.Title, new Quantity(1, "PCS"),
                service.UnitPrice, 0m, TaxPercent, InvoiceLineSource.Catalog, null);
            invoice.AddLine(serviceLine);
        }
    }

    private static Money CostOf(Product[] products, string productId, string variantId)
    {
        foreach (var product in products)
        {
            if (product.Id != productId) continue;
            foreach (var variant in product.Variants)
                if (variant.VariantId == variantId) return variant.CostPrice;
        }
        return Money.ZeroIrt;
    }

    private static PendingCommand PendingFor(string tenantId, Invoice invoice, long localSequence,
        SettlementState state, DateTimeOffset now)
    {
        var payload = $"{{\"invoiceId\":\"{invoice.Id}\"}}";
        var envelope = CommandEnvelope.Create(
            invoice.OperationId ?? IdGenerator.NewId(now), tenantId, SeedUser, "D-SEED",
            CommandTypes.SalesInvoicePost, "Invoice", invoice.Id, payload,
            CommandFingerprint.Compute(CommandTypes.SalesInvoicePost, payload), now, localSequence);
        var row = PendingCommand.Create(envelope, now);
        if (state != SettlementState.LocallyPosted)
        {
            var result = new SettlementResult(envelope.OperationId, SettlementOutcome.Accepted,
                null, null, invoice.Id, null, now, FinancialEffect.Applied, null);
            row.ApplySettlement(state, result, now);
        }
        return row;
    }
}
