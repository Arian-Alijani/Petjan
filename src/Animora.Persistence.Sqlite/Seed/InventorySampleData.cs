using Animora.Domain.Inventory;
using Animora.Domain.Primitives;
using Animora.Domain.Purchasing;
using Animora.Domain.Results;
using Animora.Domain.Time;
using Animora.Domain.ValueObjects;
using Animora.Persistence.Sqlite.Context;
using Microsoft.EntityFrameworkCore;

namespace Animora.Persistence.Sqlite.Seed;

/// <summary>
/// Optional example inventory &amp; purchasing data (C06), behind a marker and existing-product check.
/// </summary>
/// <remarks>
/// Seeds a catalogue of 45 products (2 variants each = 90 variants), one opening
/// <see cref="MovementKind.Receipt"/> per variant so on-hand starts non-zero, 6 suppliers and 8 purchase
/// orders across the lifecycle states. The receipts that advance an order to
/// <see cref="PurchaseOrderState.PartiallyReceived"/> / <see cref="PurchaseOrderState.Received"/> also
/// append their ledger movements, so the order state, the receipt rows and the on-hand snapshot agree —
/// the snapshot itself is maintained by the store's save path (C06 T03), never written here.
/// </remarks>
public sealed class InventorySampleData(AnimoraLocalDbContext db, ILocalTenantContext tenant, IClock clock)
{
    public const string SampleKey = "inventory.sample";

    private const int VariantsPerProduct = 2;

    /// <summary>Code | Name | Brand | Category | Unit | Kind | CostMinor | SaleMinor.</summary>
    private static readonly (string Code, string Name, string Brand, string Category, string Unit,
        ProductKind Kind, long Cost, long Sale)[] Catalogue =
    [
        ("MED-001", "Amoxicillin 250mg", "SamiPharm", "MED", "TAB", ProductKind.Medicine, 18_000, 32_000),
        ("MED-002", "Enrofloxacin 50mg", "SamiPharm", "MED", "TAB", ProductKind.Medicine, 24_000, 41_000),
        ("MED-003", "Metronidazole 200mg", "Behvarzan", "MED", "TAB", ProductKind.Medicine, 15_000, 28_000),
        ("MED-004", "Dexamethasone Inj.", "Behvarzan", "MED", "ML", ProductKind.Medicine, 42_000, 68_000),
        ("MED-005", "Ketoprofen Oral", "Razak", "MED", "ML", ProductKind.Medicine, 55_000, 89_000),
        ("MED-006", "Praziquantel Tab.", "SamiPharm", "MED", "TAB", ProductKind.Medicine, 31_000, 52_000),
        ("MED-007", "Omeprazole Capsule", "Razak", "MED", "PCS", ProductKind.Medicine, 27_000, 46_000),
        ("MED-008", "Furosemide Inj.", "Behvarzan", "MED", "ML", ProductKind.Medicine, 19_000, 34_000),
        ("MED-009", "Diphenhydramine", "SamiPharm", "MED", "ML", ProductKind.Medicine, 22_000, 39_000),
        ("MED-010", "Cefazolin 1g", "Razak", "MED", "PCS", ProductKind.Medicine, 61_000, 98_000),

        ("VAC-001", "Rabies Vaccine", "VacIran", "VAC", "DOSE", ProductKind.Vaccine, 180_000, 260_000),
        ("VAC-002", "DHPP Vaccine", "VacIran", "VAC", "DOSE", ProductKind.Vaccine, 210_000, 310_000),
        ("VAC-003", "FVRCP Vaccine", "VacIran", "VAC", "DOSE", ProductKind.Vaccine, 195_000, 285_000),
        ("VAC-004", "Leptospirosis", "VacIran", "VAC", "DOSE", ProductKind.Vaccine, 165_000, 245_000),
        ("VAC-005", "Bordetella", "VacIran", "VAC", "DOSE", ProductKind.Vaccine, 155_000, 230_000),

        ("FOOD-001", "Dry Food Adult 2kg", "PetFoodIran", "FOOD", "KG", ProductKind.Food, 420_000, 590_000),
        ("FOOD-002", "Dry Food Kitten 1kg", "PetFoodIran", "FOOD", "KG", ProductKind.Food, 310_000, 445_000),
        ("FOOD-003", "Wet Food Beef 400g", "PetFoodIran", "FOOD", "PCS", ProductKind.Food, 68_000, 105_000),
        ("FOOD-004", "Wet Food Chicken 400g", "PetFoodIran", "FOOD", "PCS", ProductKind.Food, 66_000, 102_000),
        ("FOOD-005", "Renal Diet 2kg", "RoyalPet", "FOOD", "KG", ProductKind.Food, 780_000, 1_050_000),
        ("FOOD-006", "Puppy Starter 1kg", "RoyalPet", "FOOD", "KG", ProductKind.Food, 360_000, 510_000),
        ("FOOD-007", "Dental Treats", "PetFoodIran", "FOOD", "BOX", ProductKind.Food, 145_000, 215_000),

        ("ACC-001", "Leather Collar M", "PetGear", "ACC", "PCS", ProductKind.PetShop, 120_000, 195_000),
        ("ACC-002", "Nylon Leash 1.5m", "PetGear", "ACC", "PCS", ProductKind.PetShop, 95_000, 160_000),
        ("ACC-003", "Pet Carrier Bag", "PetGear", "ACC", "PCS", ProductKind.PetShop, 480_000, 720_000),
        ("ACC-004", "Stainless Bowl", "PetGear", "ACC", "PCS", ProductKind.PetShop, 85_000, 145_000),

        ("SUPP-001", "Calcium Syrup 100ml", "NutriPet", "SUPP", "ML", ProductKind.PetShop, 78_000, 125_000),
        ("SUPP-002", "Omega-3 Capsules", "NutriPet", "SUPP", "BOX", ProductKind.PetShop, 210_000, 320_000),
        ("SUPP-003", "Multivitamin Drops", "NutriPet", "SUPP", "ML", ProductKind.PetShop, 96_000, 155_000),
        ("SUPP-004", "Joint Support Powder", "NutriPet", "SUPP", "G", ProductKind.PetShop, 265_000, 395_000),

        ("GROOM-001", "Medicated Shampoo 400ml", "CleanPet", "GROOM", "ML", ProductKind.PetShop, 135_000, 205_000),
        ("GROOM-002", "Detangling Conditioner", "CleanPet", "GROOM", "ML", ProductKind.PetShop, 118_000, 182_000),
        ("GROOM-003", "Nail Clipper Pro", "CleanPet", "GROOM", "PCS", ProductKind.PetShop, 92_000, 155_000),

        ("TOY-001", "Rubber Chew Ball", "PlayPet", "TOY", "PCS", ProductKind.PetShop, 55_000, 98_000),
        ("TOY-002", "Rope Tug Toy", "PlayPet", "TOY", "PCS", ProductKind.PetShop, 48_000, 88_000),
        ("TOY-003", "Cat Feather Wand", "PlayPet", "TOY", "PCS", ProductKind.PetShop, 42_000, 79_000),

        ("HYG-001", "Training Pads 50pk", "CleanPet", "HYG", "BOX", ProductKind.Consumable, 320_000, 465_000),
        ("HYG-002", "Litter Sand 5kg", "CleanPet", "HYG", "KG", ProductKind.Consumable, 185_000, 275_000),
        ("HYG-003", "Ear Cleaning Solution", "CleanPet", "HYG", "ML", ProductKind.Consumable, 74_000, 122_000),

        ("LAB-001", "Blood Collection Tube", "LabMed", "LAB", "BOX", ProductKind.Consumable, 240_000, 360_000),
        ("LAB-002", "Rapid Test Kit", "LabMed", "LAB", "BOX", ProductKind.Consumable, 520_000, 780_000),

        ("SURG-001", "Sterile Gauze 10x10", "SurgiCare", "SURG", "BOX", ProductKind.Consumable, 165_000, 248_000),
        ("SURG-002", "Suture Thread 3-0", "SurgiCare", "SURG", "PCS", ProductKind.Consumable, 195_000, 295_000),
        ("SURG-003", "Disposable Gloves M", "SurgiCare", "SURG", "BOX", ProductKind.Consumable, 148_000, 225_000),

        ("EQUIP-001", "Digital Thermometer", "MediTech", "EQUIP", "PCS", ProductKind.Equipment, 320_000, 495_000),
    ];

    private static readonly (string Code, string Name, string Contact, string Phone, int TermDays)[] SupplierRows =
    [
        ("SUP-01", "پخش دارویی سپهر", "آقای رضایی", "021-88001100", 30),
        ("SUP-02", "پت‌فود ایران", "خانم کریمی", "021-88992200", 15),
        ("SUP-03", "تجهیز طب آریا", "آقای موسوی", "021-77665544", 0),
        ("SUP-04", "واکسن‌پخش پارس", "خانم احمدی", "021-66554433", 45),
        ("SUP-05", "لوازم جانبی پت‌گیر", "آقای نادری", "021-55443322", 10),
        ("SUP-06", "آزمایشگاه مهر", "خانم شریفی", "021-44332211", 20),
    ];

    // The default service catalogue (LocalSeeder.DefaultServicesKey) is seeded before this block and also
    // creates Products, so only stock-keeping (non-service) products prove the inventory sample ran.
    public bool AlreadySeeded() => db.Set<Product>().IgnoreQueryFilters().Any(p => p.Kind != ProductKind.Service);

    public void Populate()
    {
        var tenantId = tenant.TenantId;
        var now = clock.UtcNow;

        var categories = db.Set<ProductCategory>().AsNoTracking().ToArray();
        if (categories.Length == 0) return;
        var categoryByCode = new Dictionary<string, string>(categories.Length, StringComparer.Ordinal);
        for (var i = 0; i < categories.Length; i++) categoryByCode[categories[i].Code] = categories[i].Id;

        var suppliers = AddSuppliers(tenantId);
        var products = AddProducts(tenantId, categoryByCode, now);
        if (products.Count == 0) return;

        AddPurchaseOrders(tenantId, suppliers, products, now);
    }

    private Supplier[] AddSuppliers(string tenantId)
    {
        var suppliers = new Supplier[SupplierRows.Length];
        for (var i = 0; i < SupplierRows.Length; i++)
        {
            var row = SupplierRows[i];
            var created = Supplier.Create(tenantId, row.Code, row.Name, row.TermDays,
                contactPerson: row.Contact, phone: row.Phone);
            if (created.IsFailure) continue;
            suppliers[i] = created.Value!;
            db.Add(suppliers[i]);
        }
        return suppliers;
    }

    private List<Product> AddProducts(
        string tenantId, Dictionary<string, string> categoryByCode, DateTimeOffset now)
    {
        var products = new List<Product>(Catalogue.Length);
        for (var i = 0; i < Catalogue.Length; i++)
        {
            var row = Catalogue[i];
            if (!categoryByCode.TryGetValue(row.Category, out var categoryId)) continue;

            var variants = new List<ProductVariant>(VariantsPerProduct);
            for (var v = 0; v < VariantsPerProduct; v++)
            {
                variants.Add(new ProductVariant(
                    IdGenerator.NewId(now), $"{row.Code}-{v + 1}", "SIZE",
                    v == 0 ? "SMALL" : "LARGE",
                    Money.Irt(row.Cost + v * 10_000L), Money.Irt(row.Sale + v * 15_000L),
                    new Quantity(10 + v * 5, row.Unit), Barcode: null, IsActive: true));
            }

            var created = Product.Create(tenantId, row.Code, row.Name, categoryId, row.Kind, row.Unit,
                Money.Irt(row.Cost), Money.Irt(row.Sale), variants,
                reorderLevel: new Quantity(10, row.Unit), brand: row.Brand);
            if (created.IsFailure) continue;

            var product = created.Value!;
            products.Add(product);
            db.Add(product);

            // Opening stock: one inbound movement per variant so on-hand starts non-zero and C09 has a basis.
            for (var v = 0; v < product.Variants.Count; v++)
            {
                var variant = product.Variants[v];
                var opening = (v + 1) * 25m + i % 7;
                var movement = StockMovement.Create(tenantId, product.Id, variant.VariantId,
                    MovementKind.Receipt, new Quantity(opening, product.UnitCode),
                    variant.CostPrice, now.AddDays(-30 + i % 20), "sample",
                    sourceEntityType: "Seed", reason: "Opening balance");
                if (movement.IsSuccess) db.Add(movement.Value!);
            }
        }
        return products;
    }

    private void AddPurchaseOrders(
        string tenantId, Supplier[] suppliers, List<Product> products, DateTimeOffset now)
    {
        var activeSuppliers = 0;
        for (var i = 0; i < suppliers.Length; i++) if (suppliers[i] is not null) activeSuppliers++;
        if (activeSuppliers == 0) return;

        Supplier Supplier(int index)
        {
            var seen = -1;
            for (var i = 0; i < suppliers.Length; i++)
            {
                if (suppliers[i] is null) continue;
                seen++;
                if (seen == index) return suppliers[i];
            }
            var fallback = -1;
            for (var i = 0; i < suppliers.Length; i++) if (suppliers[i] is not null) { fallback = i; break; }
            return suppliers[fallback];
        }

        // 8 orders across the lifecycle: Draft x2, Submitted x2, PartiallyReceived x2, Received x1, Cancelled x1.
        var plan = new (int SupplierIndex, int[] ProductIndexes, string State)[]
        {
            (0, [0, 1, 2], "draft"),
            (1, [15, 16], "draft"),
            (3, [10, 11, 12], "submitted"),
            (4, [22, 23], "submitted"),
            (2, [42, 43, 44], "partial"),
            (5, [40, 41], "partial"),
            (0, [3, 4, 6], "received"),
            (1, [17, 18, 19], "cancelled"),
        };

        for (var i = 0; i < plan.Length; i++)
        {
            var (supplierIndex, productIndexes, state) = plan[i];
            var supplier = Supplier(supplierIndex);
            var ordered = now.AddDays(-14 + i * 2);
            var created = PurchaseOrder.Create(tenantId, $"PO-{1 + i:D5}", supplier.Id, ordered, "sample",
                expectedUtc: ordered.AddDays(7));
            if (created.IsFailure) continue;

            var order = created.Value!;
            var lines = new List<PurchaseOrderLine>(productIndexes.Length);
            for (var l = 0; l < productIndexes.Length; l++)
            {
                var product = products[productIndexes[l] % products.Count];
                if (product.Variants.Count == 0) continue;
                var variant = product.Variants[l % product.Variants.Count];
                var line = new PurchaseOrderLine(IdGenerator.NewId(now), product.Id, variant.VariantId,
                    new Quantity(20 + l * 10, product.UnitCode), Quantity.Zero(product.UnitCode),
                    variant.CostPrice, DiscountPercent: 0m, TaxPercent: 9m,
                    LotNumber: $"LOT-{i:D2}{l}", ExpiryUtc: ordered.AddYears(2));
                lines.Add(line);
                var added = order.AddLine(line);
                if (added.IsFailure) { lines.RemoveAt(lines.Count - 1); }
            }
            if (lines.Count == 0) continue;

            if (state == "draft")
            {
                db.Add(order);
                continue;
            }

            var submitted = order.Submit(clock);
            if (submitted.IsFailure) continue;

            if (state is "partial" or "received")
            {
                var received = ApplySampleReceipt(tenantId, order, lines, state == "partial", now);
                if (!received) continue;
            }
            else if (state == "cancelled")
            {
                var cancelled = order.Cancel("Sample: supplier could not fulfil");
                if (cancelled.IsFailure) continue;
            }

            db.Add(order);
        }
    }

    /// <summary>
    /// Posts a receipt against <paramref name="order"/> covering half (partial) or all (full) of each line and
    /// appends the matching ledger movements, so the receipt, the order state and on-hand all agree.
    /// </summary>
    private bool ApplySampleReceipt(
        string tenantId, PurchaseOrder order, List<PurchaseOrderLine> lines, bool partial, DateTimeOffset now)
    {
        var receipt = GoodsReceipt.Create(tenantId, $"GR-{order.OrderNumber}", order.SupplierId, now, "sample",
            purchaseOrderId: order.Id);
        if (receipt.IsFailure) return false;

        var document = receipt.Value!;
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var quantity = partial
                ? new Quantity(Math.Floor(line.OrderedQuantity.Value / 2m), line.OrderedQuantity.UnitCode)
                : line.OrderedQuantity;
            if (quantity.Value <= 0m) continue;

            var addedLine = document.AddLine(new GoodsReceiptLine(IdGenerator.NewId(now), line.ProductId,
                line.VariantId, quantity, line.UnitCost, line.LotNumber, line.ExpiryUtc));
            if (addedLine.IsFailure) return false;

            var movement = StockMovement.Create(tenantId, line.ProductId, line.VariantId,
                MovementKind.Receipt, quantity, line.UnitCost, now, "sample",
                lotNumber: line.LotNumber, expiryUtc: line.ExpiryUtc,
                sourceEntityType: "GoodsReceipt", sourceEntityId: document.Id);
            if (movement.IsSuccess) db.Add(movement.Value!);
        }

        var posted = document.Post(clock);
        if (posted.IsFailure) return false;
        var applied = order.ApplyReceipt(document);
        if (applied.IsFailure) return false;

        db.Add(document);
        return true;
    }
}
