using Animora.Persistence.PostgreSql.Options;
using Animora.Server.Observability;
using Animora.Server.Tests.Persistence;
using Npgsql;
using Xunit;
namespace Animora.Server.Tests.Observability;

public sealed class QueryBudgetPgTests(PgFixture pg) : IClassFixture<PgFixture>
{
    [PgFact] public async Task Reporting_Concurrency1_Timeout5s_RowCap()
    {
        await using var source=NpgsqlDataSource.Create(pg.ConnectionString); using var budget=new QueryBudget(new ReportingQuerySessionFactory(source));
        await using var lease=await budget.Acquire(QueryClass.Reporting); using var cancellation=new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async()=>{ await using var blocked=await budget.Acquire(QueryClass.Reporting,cancellation.Token); });
        var timeout=await lease.Reporting!.ReadAsync("SHOW statement_timeout",r=>r.GetString(0)); Assert.Equal("5s",timeout.Single());
        await Assert.ThrowsAsync<InvalidOperationException>(()=>lease.Reporting.ReadAsync("SELECT generate_series(1,10001)",r=>r.GetInt32(0)));
        await using var oltp=await budget.Acquire(QueryClass.Oltp); Assert.Null(oltp.Reporting);
    }
    [PgFact] public async Task StatementTimeout_Enforced()
    {
        await using var source=NpgsqlDataSource.Create(pg.ConnectionString); using var budget=new QueryBudget(new ReportingQuerySessionFactory(source));
        await using var lease=await budget.Acquire(QueryClass.Reporting);
        await Assert.ThrowsAnyAsync<NpgsqlException>(()=>lease.Reporting!.ReadAsync("SELECT pg_sleep(10)",r=>0));
    }
}
