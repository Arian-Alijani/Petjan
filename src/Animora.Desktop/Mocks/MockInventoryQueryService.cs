using Animora.Contracts.Inventory;

namespace Animora.Desktop.Mocks;

/// <summary>
/// Deterministic in-memory implementation of <see cref="IInventoryQueryService"/>.
/// The catalog is re-derived from what the reference screen visually needs
/// (Animora-UI/assets/inventory-data.js <c>CATS</c>/<c>PRODUCTS</c>) so the
/// rebuilt product table renders the same rows, category tags, variant chips,
/// stock bars and Persian text — six categories and eight products, each with
/// one to four variants. Category/variant figures are the source of truth;
/// every derived total (stock, price range, status) is computed here, never
/// stored twice. This is the single swap point a future backend replaces
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

    private static readonly IReadOnlyList<Product> Products =
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

    public Task<IReadOnlyList<Product>> GetProductsAsync() => Task.FromResult(Products);

    public Task<IReadOnlyList<CategoryInfo>> GetCategoriesAsync() => Task.FromResult<IReadOnlyList<CategoryInfo>>(Categories);
}
