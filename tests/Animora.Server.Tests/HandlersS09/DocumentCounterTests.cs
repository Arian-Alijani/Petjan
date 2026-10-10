using Animora.Domain.Primitives;
using Animora.Persistence.PostgreSql.Repositories;
using Animora.Server.Tests.Persistence;
using Xunit;
using Microsoft.EntityFrameworkCore;
namespace Animora.Server.Tests.HandlersS09;
public sealed class DocumentCounterTests(PgFixture pg) : IClassFixture<PgFixture>
{
    [PgFact] public async Task ConcurrentAllocation_NoDuplicateNoGap()
    {
        var tenant = IdGenerator.NewId();
        await using (var db = TestSupport.CreateContext(new FixedTenantContext(tenant), pg.ConnectionString))
            await db.Database.MigrateAsync();
        var values = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var db = TestSupport.CreateContext(new FixedTenantContext(tenant), pg.ConnectionString);
            await using var tx = await db.Database.BeginTransactionAsync();
            var value = await new DocumentCounterAllocator(db).NextAsync(tenant, "invoice", "1405", default);
            await tx.CommitAsync(); return value;
        }));
        Assert.Equal(Enumerable.Range(1, 8).Select(i => (long)i), values.Order());
        await using var rollback = TestSupport.CreateContext(new FixedTenantContext(tenant), pg.ConnectionString);
        await using (var tx = await rollback.Database.BeginTransactionAsync())
        {
            Assert.Equal(9, await new DocumentCounterAllocator(rollback).NextAsync(tenant, "invoice", "1405", default));
            await tx.RollbackAsync();
        }
        await using (var tx = await rollback.Database.BeginTransactionAsync())
        {
            Assert.Equal(9, await new DocumentCounterAllocator(rollback).NextAsync(tenant, "invoice", "1405", default));
            await tx.CommitAsync();
        }
    }
}
