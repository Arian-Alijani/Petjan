using Animora.Domain.Inventory;
using Animora.Domain.ValueObjects;
using Animora.Server.Tests.Persistence;
using Xunit;
namespace Animora.Server.Tests.HandlersS10;
public sealed class StockDerivationTests
{
    [Fact] public void OnHand_IsDerived_NeverStored()
    {
        var now = DateTimeOffset.UtcNow;
        var a = StockMovement.Create("tenant", "product", "variant", MovementKind.Receipt, new Quantity(5, "PCS"), Money.Irt(10), now, "user").Value!;
        var b = StockMovement.Create("tenant", "product", "variant", MovementKind.Sale, new Quantity(-2, "PCS"), Money.Irt(10), now, "user").Value!;
        Assert.Equal(3, StockCalculator.OnHand([a, b]).Value);
        using var db = TestSupport.CreateContext();
        Assert.DoesNotContain(db.Model.GetEntityTypes().SelectMany(e => e.GetProperties()),
            p => p.Name.Contains("OnHand", StringComparison.OrdinalIgnoreCase));
    }
    [Fact] public void ConcurrentMovements_AreAdditive_NotLastWriterWins()
    {
        var now = DateTimeOffset.UtcNow;
        var movements = new[] { 2m, 3m }.Select(q => StockMovement.Create("tenant", "product", "variant",
            MovementKind.AdjustmentIn, new Quantity(q, "PCS"), Money.Irt(10), now, "user").Value!).ToArray();
        Assert.Equal(5, StockCalculator.OnHand(movements).Value);
        Assert.Equal(10, StockCalculator.WeightedAverageCost(movements).Minor);
    }
}
