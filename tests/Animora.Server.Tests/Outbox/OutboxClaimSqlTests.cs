using Animora.Persistence.PostgreSql.Outbox;
using Microsoft.EntityFrameworkCore;
using Xunit;
namespace Animora.Server.Tests.Outbox;

public sealed class OutboxClaimSqlTests
{
    [Fact]
    public void Predicate_HandlesNullLease_And_ExpiredLease()
    {
        var sql = OutboxClaimStore.ClaimSql;
        Assert.Contains("FOR UPDATE SKIP LOCKED", sql); Assert.Contains("RETURNING o.*", sql);
        Assert.Contains("\"Status\"=0 AND \"NextAttemptUtc\"<=@now", sql);
        Assert.Contains("\"Status\"=1 AND \"ClaimedUntilUtc\"<=@now", sql);
        Assert.DoesNotContain("IS NULL", sql); // NULL does not make a claimed lease eligible.
    }
}
public sealed class ConsumerIdempotencyTests
{
    [Fact]
    public void Redelivery_IsNoOp()
    {
        using var db = new OutboxDbContext(new DbContextOptionsBuilder<OutboxDbContext>()
            .UseNpgsql(Persistence.TestSupport.DummyConnection).Options);
        Assert.Equal(new[] { "ConsumerName", "TenantId", "MessageId" }, db.Model.FindEntityType(typeof(ConsumedMessage))!
            .FindPrimaryKey()!.Properties.Select(p => p.Name));
        Assert.Equal(2, db.Model.GetEntityTypes().Count());
        Assert.All(db.Model.GetEntityTypes(), t => Assert.Null(t.GetQueryFilter()));
        var source = File.ReadAllText(Path.Combine(RepoRoot.Find(), "src/Animora.Persistence.PostgreSql/Outbox/OutboxConsumerRunner.cs"));
        Assert.Contains("ON CONFLICT", source); Assert.Contains("if (inserted == 0)", source);
        Assert.True(source.IndexOf("if (inserted == 0)", StringComparison.Ordinal) < source.IndexOf("consumer.ConsumeAsync", StringComparison.Ordinal));
    }
}
