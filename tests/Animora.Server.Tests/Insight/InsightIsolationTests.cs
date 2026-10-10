using Animora.Domain.Results;
using Animora.Persistence.PostgreSql.Insight;
using Animora.Persistence.PostgreSql.Options;
using Animora.Server.Insight;
using Animora.Server.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;
namespace Animora.Server.Tests.Insight;
public sealed class InsightIsolationTests
{
    private sealed class BudgetReachedException : Exception;
    private sealed class ProbeBudget : IInsightReportingBudget
    {
        public int Calls;
        public ValueTask<ReportingQuerySession> AcquireAsync(CancellationToken ct = default)
        { Calls++; throw new BudgetReachedException(); }
    }
    private sealed class Actor : IInsightOperatorContext { public string OperatorId => "operator-one"; }
    private sealed class Provider : IInsightMetricProvider
    {
        public string Name => "future-test-metric";
        public int Calls;
        public Task<Result<IReadOnlyList<InsightRow>>> ExecuteAsync(InsightQueryContext ctx, CancellationToken ct = default)
        { Calls++; return Task.FromResult(Result<IReadOnlyList<InsightRow>>.Ok([])); }
    }
    private static InsightRequest Request => new("future-test-metric", "tenant-one", new(2026, 1, 1), new(2026, 2, 1), 1, 50);
    private static AnimoraInsightReadContext Context() => new(new DbContextOptionsBuilder<AnimoraInsightReadContext>().UseNpgsql(TestSupport.DummyConnection).Options);
    private static ReportingInsightQuery Query(ProbeBudget budget, AnimoraInsightReadContext context, Provider provider) =>
        new(budget, context, new Actor(), new InsightCache(TimeProvider.System), Options.Create(new InsightOptions { Enabled = true, ConsentAdrReference = "ADR-consent-test" }),
            [provider], new HashSet<string>(StringComparer.Ordinal) { provider.Name });
    [Fact] public async Task DefaultDisabled_ReturnsInsightNotEnabled()
    {
        var services = new ServiceCollection(); services.AddAnimoraInsight(new ConfigurationBuilder().Build());
        using var container = services.BuildServiceProvider(); using var scope = container.CreateScope();
        var query = scope.ServiceProvider.GetRequiredService<ITenantInsightQuery>(); Assert.False(query.IsEnabled);
        Assert.Equal(ErrorCodes.InsightNotEnabled, (await query.QueryAsync(Request)).Error);
        Assert.DoesNotContain(services, x => x.ServiceType == typeof(IInsightReportingBudget) || x.ServiceType == typeof(AnimoraInsightReadContext));
    }
    [Fact] public async Task EnabledWithoutConsentAdr_FailsStartup()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Insight:Enabled"] = "true", ["Insight:ConsentAdrReference"] = " " });
        builder.Services.AddAnimoraInsight(builder.Configuration);
        using var host = builder.Build();
        await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());
    }
    [Theory] [InlineData(201, 30, "future-test-metric")] [InlineData(50, 401, "future-test-metric")]
    [InlineData(50, 30, "unregistered")] [InlineData(0, 30, "future-test-metric")] [InlineData(50, -1, "future-test-metric")]
    public async Task RequestBounds_RejectedBeforeDb(int size, int days, string metric)
    {
        var budget = new ProbeBudget(); var provider = new Provider(); await using var context = Context();
        var request = Request with { Metric = metric, PageSize = size, To = Request.From.AddDays(days) };
        Assert.Equal(ErrorCodes.ValidationFailed, (await Query(budget, context, provider).QueryAsync(request)).Error);
        Assert.Equal(0, budget.Calls); Assert.Equal(0, provider.Calls);
    }
    [Fact] public async Task AlwaysAcquiresReportingLease()
    {
        var budget = new ProbeBudget(); var provider = new Provider(); await using var context = Context();
        // A sentinel at acquisition proves ordering without connecting to any database or registering a production metric.
        await Assert.ThrowsAsync<BudgetReachedException>(() => Query(budget, context, provider).QueryAsync(Request));
        Assert.Equal(1, budget.Calls); Assert.Equal(0, provider.Calls);
    }
    [Fact] public void NoOtherDbContextInjectableInInsight()
    {
        string root = Path.Combine(RepoRoot.Find(), "src/Animora.Persistence.PostgreSql/Insight");
        foreach (var file in Directory.EnumerateFiles(root, "*.cs"))
        {
            if (Path.GetFileName(file) == "AnimoraInsightReadContext.cs") continue;
            string source = File.ReadAllText(file).Replace("AnimoraInsightReadContext", "", StringComparison.Ordinal);
            Assert.DoesNotContain("DbContext", source);
            Assert.DoesNotContain("AnimoraServerDbContext", source);
        }
        foreach (var constructor in typeof(ReportingInsightQuery).GetConstructors())
            Assert.DoesNotContain(constructor.GetParameters(), p => typeof(DbContext).IsAssignableFrom(p.ParameterType) && p.ParameterType != typeof(AnimoraInsightReadContext));
    }
    [Fact] public void NotReachableFromTenantRoutes()
    {
        foreach (string area in new[] { "Auth", "Authorization", "Commands", "Handlers", "Files", "Sync" })
            foreach (var file in Directory.EnumerateFiles(Path.Combine(RepoRoot.Find(), "src/Animora.Server", area), "*.cs", SearchOption.AllDirectories))
                Assert.DoesNotContain("ITenantInsightQuery", File.ReadAllText(file));
    }
    [Fact] public void CacheKeyHasTenantUserResourceVersion()
    {
        var key = new InsightCacheKey("tenant", "operator", "metric", "v1");
        Assert.NotEqual(key, new InsightCacheKey("other", "operator", "metric", "v1"));
        Assert.NotEqual(key, new InsightCacheKey("tenant", "other", "metric", "v1"));
        Assert.NotEqual(key, new InsightCacheKey("tenant", "operator", "other", "v1"));
        Assert.NotEqual(key, new InsightCacheKey("tenant", "operator", "metric", "v2"));
        Assert.Throws<ArgumentException>(() => new InsightCacheKey("", "operator", "metric", "v1"));
        Assert.Throws<ArgumentException>(() => new InsightCacheKey("tenant", " ", "metric", "v1"));
        Assert.Throws<ArgumentException>(() => new InsightCacheKey("tenant", "operator", "", "v1"));
        Assert.Throws<ArgumentException>(() => new InsightCacheKey("tenant", "operator", "metric", ""));
    }
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 10, 10, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    [Fact] public void Cache_Expires_AndNeverCrossesTenantOperatorOrWindow()
    {
        var clock = new Clock(); var cache = new InsightCache(clock);
        var key = new InsightCacheKey("tenant", "operator", "metric", "v1:window-a");
        cache.Set(key, new([], 1, 50, false), 60);
        Assert.True(cache.TryGet(key, out _));
        Assert.False(cache.TryGet(new("other", "operator", "metric", "v1:window-a"), out _));
        Assert.False(cache.TryGet(new("tenant", "other", "metric", "v1:window-a"), out _));
        Assert.False(cache.TryGet(new("tenant", "operator", "metric", "v1:window-b"), out _));
        clock.Now += TimeSpan.FromSeconds(61); Assert.False(cache.TryGet(key, out _));
    }
}
public sealed class InsightSourceCatalogTests
{
    [Fact] public void EmptyAtLaunch() => Assert.Empty(InsightSourceCatalog.AllowedTables);
}
public sealed class InsightMetricsTests
{
    [Fact] public void NoMetricRegisteredAtLaunch() => Assert.Empty(InsightMetrics.Registered);
}
