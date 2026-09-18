using Animora.Contracts.Inventory;

namespace Animora.Desktop.Mocks;

/// <summary>
/// Deterministic, stateful in-memory implementation of
/// <see cref="IInventoryQueryService"/>. The catalog is re-derived from what the
/// reference screen visually needs (Animora-UI/assets/inventory-data.js
/// <c>CATS</c>/<c>PRODUCTS</c>/<c>MOVES</c>) so the rebuilt product table and the
/// product / new-product / stock-adjust modals render the same rows, category
/// tags, variant chips, stock bars, history ledger and Persian text — six
/// categories, eight products each with one to four variants, and the
/// reference's per-product movement ledger. Category/variant figures are the
/// source of truth; every derived total (stock, value, status) is computed here,
/// never stored twice. This is the single swap point a future backend replaces
/// (R-18) — no ViewModel or View change is required to do so.
/// </summary>
public sealed class MockInventoryQueryService : IInventoryQueryService
{
    // Reference CATS (inventory-data.js) — id/label/tone/icon.
    private static readonly CategoryInfo[] Categories =
    [
        new("med", "دارو", "teal", "IconFlask"),
        new("petshop", "پت‌شاپ", "peach", "IconBone"),
        new("vaccine", "واکسن", "lilac", "IconSyringe"),
        new("food", "غذا", "amber", "IconBone"),
        new("tool", "ابزار درمانی", "blue", "IconTools"),
        new("care", "بهداشتی", "green", "IconBath"),
    ];

    // Reference PRODUCTS (inventory-data.js) — the live, mutable store.
    private readonly List<Product> _products =
    [
        new Product(
            "p1", "AN-1082", "قرص ضد انگل دورامکتین", "BioVet", "med",
            "💊", "a",
            "قرص خوراکی ضد انگل داخلی و خارجی برای سگ و گربه.",
            new UnitInfo("دوز", ["۵ mg", "۱۰ mg", "۲۰ mg"]),
            [
                new ProductVariant("v1a", "۵ mg", "AN-1082-05", 42, 15, 95_000, 130_000, "۱۴۰۶/۰۳/۱۵"),
                new ProductVariant("v1b", "۱۰ mg", "AN-1082-10", 3, 10, 120_000, 160_000, "۱۴۰۶/۰۳/۱۵"),
                new ProductVariant("v1c", "۲۰ mg", "AN-1082-20", 12, 8, 180_000, 240_000, "۱۴۰۶/۰۳/۱۵"),
            ]),

        new Product(
            "p2", "RC-1147", "غذای گربه رویال کنین", "Royal Canin", "food",
            "🥫", "b",
            "غذای خشک استریل ویژه گربه‌های بالغ عقیم‌شده.",
            new UnitInfo("وزن", ["۲ کیلوگرم", "۴ کیلوگرم", "۸ کیلوگرم", "۱۵ کیلوگرم"]),
            [
                new ProductVariant("v2a", "۲ کیلوگرم", "RC-1147-2", 24, 10, 620_000, 780_000, "۱۴۰۶/۰۸/۲۰"),
                new ProductVariant("v2b", "۴ کیلوگرم", "RC-1147-4", 42, 15, 980_000, 1_250_000, "۱۴۰۶/۰۸/۲۰"),
                new ProductVariant("v2c", "۸ کیلوگرم", "RC-1147-8", 18, 8, 1_780_000, 2_250_000, "۱۴۰۶/۰۸/۲۰"),
                new ProductVariant("v2d", "۱۵ کیلوگرم", "RC-1147-15", 6, 5, 3_050_000, 3_800_000, "۱۴۰۶/۰۸/۲۰"),
            ]),

        new Product(
            "p3", "VC-1205", "واکسن هاری", "Rabisin", "vaccine",
            "💉", "c",
            "واکسن غیرفعال ضد هاری، تک‌دوز داخل عضلانی.",
            null,
            [
                new ProductVariant("v3a", "تک‌دوز ۱ml", "VC-1205", 4, 15, 320_000, 480_000, "۱۴۰۵/۱۱/۱۸"),
            ]),

        new Product(
            "p4", "SH-1318", "شامپو ضد کنه و شپش", "CleanPet", "care",
            "🧴", "d",
            "شامپو موضعی برای از بین بردن انگل خارجی.",
            new UnitInfo("حجم", ["۲۵۰ml", "۵۰۰ml", "۱ لیتر"]),
            [
                new ProductVariant("v4a", "۲۵۰ml", "SH-1318-250", 18, 10, 280_000, 420_000, "۱۴۰۷/۰۲/۰۵"),
                new ProductVariant("v4b", "۵۰۰ml", "SH-1318-500", 22, 8, 490_000, 720_000, "۱۴۰۷/۰۲/۰۵"),
                new ProductVariant("v4c", "۱ لیتر", "SH-1318-1L", 5, 6, 820_000, 1_180_000, "۱۴۰۷/۰۲/۰۵"),
            ]),

        new Product(
            "p5", "AM-1406", "شربت آنتی‌بیوتیک آموکسی‌سیلین", "Pharma", "med",
            "🧪", "e",
            "آنتی‌بیوتیک وسیع‌الطیف خوراکی.",
            new UnitInfo("حجم", ["۶۰ml", "۱۲۰ml"]),
            [
                new ProductVariant("v5a", "۶۰ml", "AM-1406-60", 14, 10, 180_000, 260_000, "۱۴۰۶/۰۵/۱۰"),
                new ProductVariant("v5b", "۱۲۰ml", "AM-1406-120", 5, 12, 210_000, 280_000, "۱۴۰۶/۰۵/۱۰"),
            ]),

        new Product(
            "p6", "TY-1520", "اسباب‌بازی توپ لاستیکی", "Kong", "petshop",
            "🎾", "f",
            "توپ مقاوم برای بازی و آموزش.",
            new UnitInfo("سایز", ["کوچک", "متوسط", "بزرگ"]),
            [
                new ProductVariant("v6a", "کوچک", "TY-1520-S", 34, 15, 85_000, 140_000, null),
                new ProductVariant("v6b", "متوسط", "TY-1520-M", 28, 12, 120_000, 190_000, null),
                new ProductVariant("v6c", "بزرگ", "TY-1520-L", 15, 10, 165_000, 260_000, null),
            ]),

        new Product(
            "p7", "SR-1670", "سرنگ یک‌بار مصرف", "MediTools", "tool",
            "🩺", "a",
            "سرنگ استریل با سرسوزن جداشونده.",
            new UnitInfo("حجم", ["۱ml", "۲.۵ml", "۵ml", "۱۰ml"]),
            [
                new ProductVariant("v7a", "۱ml", "SR-1670-1", 240, 100, 4_500, 8_000, "۱۴۰۸/۰۱/۰۱"),
                new ProductVariant("v7b", "۲.۵ml", "SR-1670-25", 180, 80, 5_200, 9_500, "۱۴۰۸/۰۱/۰۱"),
                new ProductVariant("v7c", "۵ml", "SR-1670-5", 90, 60, 6_000, 11_000, "۱۴۰۸/۰۱/۰۱"),
                new ProductVariant("v7d", "۱۰ml", "SR-1670-10", 42, 40, 7_800, 14_000, "۱۴۰۸/۰۱/۰۱"),
            ]),

        new Product(
            "p8", "VT-1811", "ویتامین B کمپلکس", "VetLab", "med",
            "💊", "b",
            "مکمل ویتامین B برای تقویت اشتها و سلامت پوست.",
            null,
            [
                new ProductVariant("v8a", "۳۰ عددی", "VT-1811", 68, 20, 145_000, 210_000, "۱۴۰۷/۰۶/۲۰"),
            ]),
    ];

    // Reference MOVES (inventory-data.js) — seeded ledger, newest first.
    private readonly List<StockMovement> _moves =
    [
        new("m1", "p1", "v1b", "in", 20, "خرید PO-14052001", "انبار مرکزی", "۱۴۰۵/۰۸/۰۲", "۰۹:۱۲", null),
        new("m2", "p1", "v1a", "out", 3, "فروش صندوق INV-1058", "صندوق‌دار", "۱۴۰۵/۰۸/۰۵", "۱۱:۴۰", null),
        new("m3", "p1", "v1a", "out", 2, "مصرف در ویزیت #۲۲۰۴", "دکتر مرادی", "۱۴۰۵/۰۸/۰۷", "۱۶:۰۵", null),
        new("m4", "p1", "v1b", "adjust", -4, "اصلاح شمارش انبار", "مدیر انبار", "۱۴۰۵/۰۸/۱۰", "۱۸:۳۰", "مغایرت در انبارگردانی"),
        new("m5", "p1", "v1c", "in", 12, "خرید PO-14052033", "انبار مرکزی", "۱۴۰۵/۰۸/۱۴", "۱۰:۲۰", null),
        new("m6", "p2", "v2b", "in", 30, "خرید PO-14052008", "انبار مرکزی", "۱۴۰۵/۰۸/۰۹", "۱۳:۱۵", null),
        new("m7", "p2", "v2a", "out", 6, "فروش صندوق INV-1071", "صندوق‌دار", "۱۴۰۵/۰۸/۱۱", "۰۹:۵۰", null),
        new("m8", "p2", "v2d", "out", 1, "فروش صندوق INV-1080", "صندوق‌دار", "۱۴۰۵/۰۸/۱۲", "۱۷:۲۵", null),
        new("m9", "p3", "v3a", "in", 15, "خرید PO-14052001", "انبار مرکزی", "۱۴۰۵/۰۸/۰۲", "۰۹:۱۲", null),
        new("m10", "p3", "v3a", "out", 8, "واکسیناسیون گروهی", "دکتر رحیمی", "۱۴۰۵/۰۸/۰۶", "۱۴:۰۰", null),
        new("m11", "p3", "v3a", "out", 3, "مصرف در ویزیت #۲۲۳۰", "دکتر مرادی", "۱۴۰۵/۰۸/۱۳", "۱۰:۳۰", null),
        new("m12", "p5", "v5b", "in", 10, "خرید PO-14052001", "انبار مرکزی", "۱۴۰۵/۰۸/۰۲", "۰۹:۱۲", null),
        new("m13", "p5", "v5a", "out", 4, "فروش صندوق INV-1065", "صندوق‌دار", "۱۴۰۵/۰۸/۰۸", "۱۲:۱۰", null),
        new("m14", "p7", "v7a", "in", 500, "خرید PO-14052012", "انبار مرکزی", "۱۴۰۵/۰۸/۱۳", "۰۸:۳۰", null),
        new("m15", "p7", "v7c", "in", 200, "خرید PO-14052012", "انبار مرکزی", "۱۴۰۵/۰۸/۱۳", "۰۸:۳۰", null),
        new("m16", "p7", "v7a", "out", 40, "مصرف درمانگاه", "پرستار", "۱۴۰۵/۰۸/۱۵", "۱۹:۰۰", null),
        new("m17", "p8", "v8a", "in", 20, "خرید PO-14052015", "انبار مرکزی", "۱۴۰۵/۰۸/۱۶", "۱۱:۰۰", null),
        new("m18", "p8", "v8a", "out", 5, "فروش صندوق INV-1090", "صندوق‌دار", "۱۴۰۵/۰۸/۱۸", "۱۵:۴۵", null),
    ];

    // Reference tones cycle for a freshly-created product (inventory.js tones[]).
    private static readonly string[] Tones = ["a", "b", "c", "d", "e", "f"];

    public Task<IReadOnlyList<Product>> GetProductsAsync() =>
        Task.FromResult<IReadOnlyList<Product>>(_products.ToList());

    public Task<IReadOnlyList<CategoryInfo>> GetCategoriesAsync() =>
        Task.FromResult<IReadOnlyList<CategoryInfo>>(Categories);

    public Task<ProductDetail?> GetDetailAsync(string productId)
    {
        var product = _products.FirstOrDefault(p => p.Id == productId);
        if (product is null)
        {
            return Task.FromResult<ProductDetail?>(null);
        }

        var movements = _moves.Where(m => m.ProductId == productId).ToList();
        return Task.FromResult<ProductDetail?>(new ProductDetail(product, movements));
    }

    public Task SaveProductInfoAsync(string productId, ProductInfoUpdate update)
    {
        var index = _products.FindIndex(p => p.Id == productId);
        if (index < 0)
        {
            return Task.CompletedTask;
        }

        var existing = _products[index];
        var attribute = string.IsNullOrWhiteSpace(update.AttributeName)
            ? null
            : new UnitInfo(update.AttributeName!, update.Variants.Select(v => v.Label).ToList());

        _products[index] = existing with
        {
            Name = update.Name,
            Code = update.Code,
            Brand = update.Brand,
            CategoryId = update.CategoryId,
            Glyph = update.Glyph,
            Description = update.Description,
            Attribute = attribute,
            Variants = update.Variants.ToList(),
        };

        return Task.CompletedTask;
    }

    public Task<ProductVariant> AddVariantAsync(string productId, NewVariantRequest request)
    {
        var index = _products.FindIndex(p => p.Id == productId);
        if (index < 0)
        {
            return Task.FromResult(new ProductVariant(
                "v_" + Guid.NewGuid().ToString("N")[..6], request.Label, request.Label, 0, 5, 0, 0, null));
        }

        var product = _products[index];
        var variant = MakeVariant(product, request.Label, product.Variants.Count);
        var variants = product.Variants.ToList();
        variants.Add(variant);

        var attribute = product.Attribute;
        if (attribute is not null && !attribute.Options.Contains(request.Label))
        {
            var options = attribute.Options.ToList();
            options.Add(request.Label);
            attribute = attribute with { Options = options };
        }

        _products[index] = product with { Variants = variants, Attribute = attribute };
        return Task.FromResult(variant);
    }

    public Task<IReadOnlyList<StockMovement>> GetHistoryAsync(string productId) =>
        Task.FromResult<IReadOnlyList<StockMovement>>(
            _moves.Where(m => m.ProductId == productId).ToList());

    public Task<Product> CreateProductAsync(NewProductRequest request)
    {
        var id = "p_" + Guid.NewGuid().ToString("N")[..5];
        var code = string.IsNullOrWhiteSpace(request.Code) ? GenerateCode() : request.Code.Trim();

        UnitInfo? attribute = null;
        var variants = request.Variants.ToList();

        if (!string.IsNullOrWhiteSpace(request.AttributeName))
        {
            var options = request.AttributeOptions.Count > 0 ? request.AttributeOptions : ["گزینه ۱"];
            attribute = new UnitInfo(request.AttributeName!, options);
            var seed = new Product(id, code, request.Name, request.Brand, request.CategoryId,
                request.Glyph, "a", request.Description, null, []);
            variants = options.Select((o, i) => MakeVariant(seed, o, i)).ToList();
        }

        var product = new Product(
            id, code, request.Name,
            string.IsNullOrWhiteSpace(request.Brand) ? "—" : request.Brand,
            request.CategoryId,
            string.IsNullOrWhiteSpace(request.Glyph) ? "📦" : request.Glyph,
            Tones[_products.Count % Tones.Length],
            request.Description,
            attribute,
            variants);

        _products.Insert(0, product);
        return Task.FromResult(product);
    }

    public Task DeleteProductAsync(string productId)
    {
        _products.RemoveAll(p => p.Id == productId);
        _moves.RemoveAll(m => m.ProductId == productId);
        return Task.CompletedTask;
    }

    public Task AdjustStockAsync(string productId, string variantId, StockAdjustRequest request)
    {
        var index = _products.FindIndex(p => p.Id == productId);
        if (index < 0)
        {
            return Task.CompletedTask;
        }

        var product = _products[index];
        var vIndex = product.Variants.ToList().FindIndex(v => v.Id == variantId);
        if (vIndex < 0)
        {
            return Task.CompletedTask;
        }

        var variants = product.Variants.ToList();
        var variant = variants[vIndex];
        var stock = Math.Max(0, request.Stock);
        var delta = stock - variant.Stock;

        variants[vIndex] = variant with { Stock = stock };
        _products[index] = product with { Variants = variants };

        if (delta != 0)
        {
            _moves.Insert(0, new StockMovement(
                "m_" + Guid.NewGuid().ToString("N")[..6],
                productId, variantId, "adjust", delta,
                "اصلاح موجودی دستی", "شما", "۱۴۰۵/۰۸/۲۰", "—",
                "ثبت از طریق «تغییر موجودی»"));
        }

        return Task.CompletedTask;
    }

    // Reference makeVariant(p, label, idx): SKU derives from the product code.
    private static ProductVariant MakeVariant(Product product, string label, int index) =>
        new(
            "v_" + Guid.NewGuid().ToString("N")[..6],
            label,
            $"{product.Code}-{(index + 1).ToString().PadLeft(2, '0')}",
            0, 5, 0, 0, null);

    private static string GenerateCode() => "NP-" + Random.Shared.Next(1000, 10_000);
}
