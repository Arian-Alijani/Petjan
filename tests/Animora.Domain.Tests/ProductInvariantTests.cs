using System;
using System.Collections.Generic;
using Animora.Domain.Primitives;
using Animora.Domain.Inventory;
using Animora.Domain.Results;
using Animora.Domain.ValueObjects;
using Xunit;

namespace Animora.Domain.Tests;

/// <summary>
/// Product invariants: a service is never stock-tracked, a stock-tracked product keeps at least one
/// active variant, reorder levels use the product's unit, SKUs are unique inside the product, and
/// prices are set with a single currency.
/// </summary>
public sealed class ProductInvariantTests
{
    private static ProductVariant Variant(string sku, string unit = "EA", bool active = true) =>
        new(IdGenerator.NewId(DateTimeOffset.UnixEpoch), sku, ProductVariant.DefaultAttributeName, "V",
            Money.Irt(1_000), Money.Irt(2_000), Quantity.Zero(unit), null, active);

    private static Result<Product> CreateService(bool trackStock) =>
        Product.Create("T1", "SVC-1", "Grooming", "SRV", ProductKind.Service, "EA",
            Money.Irt(0), Money.Irt(50_000), [], trackStock: trackStock);

    [Fact]
    public void Service_IsNeverStockTracked()
    {
        var result = CreateService(trackStock: true);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCodes.ServiceNoStock, result.Error);

        Assert.True(CreateService(trackStock: false).IsSuccess);
    }

    [Fact]
    public void StockTrackedProduct_RequiresAtLeastOneVariant()
    {
        var result = Product.Create("T1", "MED-1", "Amoxicillin", "MED", ProductKind.Medicine, "EA",
            Money.Irt(1_000), Money.Irt(2_000), []);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCodes.VariantRequired, result.Error);
    }

    [Fact]
    public void ReorderLevel_MustMatchTheProductUnit()
    {
        var result = Product.Create("T1", "MED-2", "Drug", "MED", ProductKind.Medicine, "EA",
            Money.Irt(1_000), Money.Irt(2_000), [Variant("V1")], reorderLevel: new Quantity(5m, "ML"));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCodes.UnitMismatch, result.Error);
    }

    [Fact]
    public void VariantSku_MustBeUniqueInsideTheProduct()
    {
        var result = Product.Create("T1", "MED-3", "Drug", "MED", ProductKind.Medicine, "EA",
            Money.Irt(1_000), Money.Irt(2_000), [Variant("DUP"), Variant("DUP")]);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCodes.ProductSkuDuplicate, result.Error);
    }

    [Fact]
    public void LastActiveVariant_OfAStockTrackedProduct_CannotBeRetired()
    {
        var product = Product.Create("T1", "MED-4", "Drug", "MED", ProductKind.Medicine, "EA",
            Money.Irt(1_000), Money.Irt(2_000), [Variant("V1")]).Value!;

        var result = product.DeactivateVariant(product.Variants[0].VariantId);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCodes.VariantRequired, result.Error);
    }

    [Fact]
    public void SetPrices_RequiresASingleCurrency()
    {
        var product = Product.Create("T1", "MED-5", "Drug", "MED", ProductKind.Medicine, "EA",
            Money.Irt(1_000), Money.Irt(2_000), [Variant("V1")]).Value!;

        Assert.True(product.SetPrices(Money.Irt(1_500), Money.Irt(2_500)).IsSuccess);
        Assert.Equal(Money.Irt(1_500), product.DefaultCostPrice);

        var mismatched = product.SetPrices(Money.Irt(1_500), Money.Of(2_500, "USD"));
        Assert.True(mismatched.IsFailure);
        Assert.Equal(ErrorCodes.ValidationFailed, mismatched.Error);
    }

    [Fact]
    public void SetReorderLevel_RejectsUnitMismatchAndNegative()
    {
        var product = Product.Create("T1", "MED-6", "Drug", "MED", ProductKind.Medicine, "EA",
            Money.Irt(1_000), Money.Irt(2_000), [Variant("V1")]).Value!;

        Assert.True(product.SetReorderLevel(new Quantity(5m, "EA")).IsSuccess);

        var mismatch = product.SetReorderLevel(new Quantity(5m, "ML"));
        Assert.True(mismatch.IsFailure);
        Assert.Equal(ErrorCodes.UnitMismatch, mismatch.Error);

        var negative = product.SetReorderLevel(new Quantity(-1m, "EA"));
        Assert.True(negative.IsFailure);
        Assert.Equal(ErrorCodes.ValidationFailed, negative.Error);
    }
}
