using System.Text;
using System.Text.Json;
using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Domain.Subscriptions;
using Animora.Persistence.PostgreSql.Audit;
using Animora.Persistence.PostgreSql.Identity;
using Animora.Persistence.PostgreSql.Operator;
using Animora.Persistence.PostgreSql.Options;
using Animora.Server.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;
namespace Animora.Server.Tests.Admin;
public sealed class AuditQueriesTests(PgFixture pg) : IClassFixture<PgFixture>
{
    private sealed class Reporting(NpgsqlDataSource source) : IReportingQuerySessionFactory
    {
        public int Calls;
        public Task<ReportingQuerySession> OpenAsync(CancellationToken ct = default)
        { Calls++; return ReportingQuerySession.OpenAsync(source, ct); }
    }
    private AnimoraOperatorDbContext Context() => new(new DbContextOptionsBuilder<AnimoraOperatorDbContext>().UseNpgsql(pg.ConnectionString).Options);
    [PgFact] public async Task TenantAudit_ExplicitTenantPredicate_RangeCap_Paging_DetailCap_NoTracking()
    {
        await using (var schema = TestSupport.CreateContext(connection: pg.ConnectionString)) await schema.Database.MigrateAsync();
        string tenant = IdGenerator.NewId(), other = IdGenerator.NewId(); var now = DateTimeOffset.UtcNow;
        await using (var seed = Context())
        {
            for (int i = 0; i < 3; i++) seed.SecurityAudit.Add(SecurityAuditEntry.Create(IdGenerator.NewId(), tenant, now.AddSeconds(-i), "auth", "auth.login", "success", "safe", detailJson: JsonSerializer.Serialize(new { note = new string('گ', 3000) })));
            seed.SecurityAudit.Add(SecurityAuditEntry.Create(IdGenerator.NewId(), other, now, "auth", "auth.login", "success", "other-tenant", detailJson: "{}"));
            await seed.SaveChangesAsync();
        }
        await using var source = NpgsqlDataSource.Create(pg.ConnectionString); var reporting = new Reporting(source);
        await using var db = Context(); var query = new AuditQueries(db, reporting);
        Assert.Equal(ErrorCodes.ValidationFailed, (await query.TenantAsync("", now.AddDays(-1), now.AddDays(1), 1, 50)).Error);
        Assert.Equal(ErrorCodes.ValidationFailed, (await query.TenantAsync(tenant, now.AddDays(-91), now, 1, 50)).Error);
        var first = (await query.TenantAsync(tenant, now.AddDays(-1), now.AddDays(1), 1, 2)).Value!;
        var second = (await query.TenantAsync(tenant, now.AddDays(-1), now.AddDays(1), 2, 2)).Value!;
        Assert.Equal(3, first.Total); Assert.Equal(2, first.Items.Count); Assert.Single(second.Items);
        Assert.All(first.Items.Concat(second.Items), row =>
        {
            Assert.Equal(tenant, row.TenantId); Assert.True(Encoding.UTF8.GetByteCount(row.Detail ?? "") <= 2048);
            Assert.DoesNotContain("other-tenant", row.CorrelationId);
        });
        Assert.Empty(db.ChangeTracker.Entries()); Assert.Equal(0, reporting.Calls);
    }
    [PgFact] public async Task OperatorAudit_FilteredPaging_UnfilteredCountAlwaysUsesReportingSession()
    {
        await using (var schema = TestSupport.CreateContext(connection: pg.ConnectionString)) await schema.Database.MigrateAsync();
        string tenant = IdGenerator.NewId(); var now = DateTimeOffset.UtcNow;
        await using (var seed = Context())
        {
            for (int i = 0; i < 3; i++) seed.Audit.Add(OperatorAudit.Create(now.AddSeconds(-i), new(ActorKind.Operator, IdGenerator.NewId()), "files.mismatch.requeue", tenant, "safe", detail: new { fileId = "safe" }));
            await seed.SaveChangesAsync();
        }
        await using var source = NpgsqlDataSource.Create(pg.ConnectionString); var reporting = new Reporting(source);
        await using var db = Context(); var query = new AuditQueries(db, reporting);
        var filtered = await query.OperatorAsync("files.mismatch.requeue", tenant, now.AddDays(-1), now.AddDays(1), 1, 2);
        Assert.True(filtered.IsSuccess); Assert.Equal(3, filtered.Value!.Total); Assert.Equal(2, filtered.Value.Items.Count); Assert.Equal(0, reporting.Calls);
        var unfiltered = await query.OperatorAsync(null, null, null, null, 1, 1);
        Assert.True(unfiltered.IsSuccess); Assert.Equal(1, reporting.Calls); Assert.Single(unfiltered.Value!.Items);
    }
}
