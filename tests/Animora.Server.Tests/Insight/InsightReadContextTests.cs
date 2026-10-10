using Animora.Persistence.PostgreSql.Insight;
using Animora.Persistence.PostgreSql.Options;
using Animora.Server.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;
namespace Animora.Server.Tests.Insight;
public sealed class InsightReadContextTests(PgFixture pg) : IClassFixture<PgFixture>
{
    private sealed class Factory(NpgsqlDataSource source) : IReportingQuerySessionFactory
    {
        public Task<ReportingQuerySession> OpenAsync(CancellationToken ct = default) => ReportingQuerySession.OpenAsync(source, ct);
    }
    [PgFact] public async Task EmptyProjectedModel_ReadOnlyWorkerSession_TimeoutAndRowsCapped()
    {
        await using var source = NpgsqlDataSource.Create(pg.ConnectionString);
        await using var session = await ReportingQuerySession.OpenAsync(source, default);
        await using var context = new AnimoraInsightReadContext(new DbContextOptionsBuilder<AnimoraInsightReadContext>().UseNpgsql(source).Options);
        await context.BindAsync(session);
        Assert.Empty(context.Model.GetEntityTypes());
        Assert.Equal("on", (await session.ReadAsync("SHOW transaction_read_only", r => r.GetString(0)))[0]);
        Assert.Equal("5s", (await session.ReadAsync("SHOW statement_timeout", r => r.GetString(0)))[0]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.ReadAsync("SELECT generate_series(1,10001)", r => r.GetInt32(0)));
    }
    [PgFact] public async Task ReportingLease_IsSerialized_ReleasedByDispose_AndCancellationSafe()
    {
        await using var source = NpgsqlDataSource.Create(pg.ConnectionString);
        using var budget = new InsightReportingBudget(new Factory(source));
        var first = await budget.AcquireAsync();
        try
        {
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => { await budget.AcquireAsync(cancelled.Token); });
        }
        finally { await first.DisposeAsync(); }
        await first.DisposeAsync(); // Idempotent release must not overfill the semaphore.
        await using var second = await budget.AcquireAsync();
        Assert.Equal("on", (await second.ReadAsync("SHOW transaction_read_only", r => r.GetString(0)))[0]);
    }
}
