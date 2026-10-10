using Animora.Domain.Inventory;
using Animora.Domain.ValueObjects;
using Animora.Domain.Primitives;
using Animora.Server.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;
namespace Animora.Server.Tests.HandlersS10;
public sealed class StockImmutabilityTests(PgFixture pg) : IClassFixture<PgFixture>
{
    [PgFact] public async Task Update_Delete_Raise()
    {
        var tenant = IdGenerator.NewId();
        await using var db = TestSupport.CreateContext(new FixedTenantContext(tenant), pg.ConnectionString);
        await db.Database.MigrateAsync();
        var movement = StockMovement.Create(tenant, "product", "variant", MovementKind.Receipt, new Quantity(1, "PCS"), Money.Irt(10), DateTimeOffset.UtcNow, "user").Value!;
        db.Add(movement); await db.SaveChangesAsync();
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE public.\"StockMovements\" SET \"Reason\" = 'changed' WHERE \"Id\" = {movement.Id}"));
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM public.\"StockMovements\" WHERE \"Id\" = {movement.Id}"));
    }
}
