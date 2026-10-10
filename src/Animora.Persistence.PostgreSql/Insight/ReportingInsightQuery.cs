using Animora.Domain.Results;
using Animora.Persistence.PostgreSql.Options;
using Microsoft.Extensions.Options;
namespace Animora.Persistence.PostgreSql.Insight;
public sealed class ReportingInsightQuery : ITenantInsightQuery
{
    private readonly IInsightReportingBudget _budget;
    private readonly AnimoraInsightReadContext _context;
    private readonly IInsightOperatorContext _actor;
    private readonly InsightCache _cache;
    private readonly InsightOptions _options;
    private readonly IReadOnlyDictionary<string, IInsightMetricProvider> _providers;
    private readonly IReadOnlySet<string> _metrics;
    public bool IsEnabled => true;
    public ReportingInsightQuery(IInsightReportingBudget budget, AnimoraInsightReadContext context,
        IInsightOperatorContext actor, InsightCache cache, IOptions<InsightOptions> options, IEnumerable<IInsightMetricProvider> providers)
        : this(budget, context, actor, cache, options, providers, InsightMetrics.Registered) { }
    internal ReportingInsightQuery(IInsightReportingBudget budget, AnimoraInsightReadContext context,
        IInsightOperatorContext actor, InsightCache cache, IOptions<InsightOptions> options,
        IEnumerable<IInsightMetricProvider> providers, IReadOnlySet<string> metrics)
    {
        _budget = budget; _context = context; _actor = actor; _cache = cache; _options = options.Value;
        _providers = providers.ToDictionary(x => x.Name, StringComparer.Ordinal); _metrics = metrics;
    }
    public async Task<Result<InsightPage<InsightRow>>> QueryAsync(InsightRequest request, CancellationToken ct = default)
    {
        if (!_options.Enabled || string.IsNullOrWhiteSpace(_options.ConsentAdrReference))
            return Result<InsightPage<InsightRow>>.Fail(ErrorCodes.InsightNotEnabled);
        if (request.Page < 1 || request.Page > ReportingQuerySession.MaxRows || request.PageSize is < 1 or > 200 ||
            request.To < request.From || request.To.DayNumber - request.From.DayNumber > 400 ||
            string.IsNullOrWhiteSpace(request.Metric) || !_metrics.Contains(request.Metric) ||
            request.TenantId is { } tenant && (string.IsNullOrWhiteSpace(tenant) || tenant.Length > 26) ||
            !_providers.TryGetValue(request.Metric, out var provider))
            return Result<InsightPage<InsightRow>>.Fail(ErrorCodes.ValidationFailed);
        string user = _actor.OperatorId; // Cookie-authenticated operator identity; never a caller-supplied user id.
        var key = new InsightCacheKey(request.TenantId ?? "*", user, request.Metric,
            $"v1:{request.From.DayNumber}:{request.To.DayNumber}:{request.Page}:{request.PageSize}");
        if (_cache.TryGet(key, out var cached) && cached is not null) return Result<InsightPage<InsightRow>>.Ok(cached);
        await using var lease = await _budget.AcquireAsync(ct).ConfigureAwait(false);
        await _context.BindAsync(lease, ct).ConfigureAwait(false);
        Result<IReadOnlyList<InsightRow>> result;
        try { result = await provider.ExecuteAsync(new(request, _context), ct).ConfigureAwait(false); }
        finally { await _context.UnbindAsync(CancellationToken.None).ConfigureAwait(false); }
        if (result.IsFailure) return Result<InsightPage<InsightRow>>.Fail(result.Error);
        if (result.Value is not { } rows || rows.Count > ReportingQuerySession.MaxRows)
            return Result<InsightPage<InsightRow>>.Fail(ErrorCodes.ValidationFailed);
        long offset = (long)(request.Page - 1) * request.PageSize;
        var page = new InsightPage<InsightRow>(Array.AsReadOnly(rows.Skip((int)Math.Min(offset, rows.Count)).Take(request.PageSize).ToArray()),
            request.Page, request.PageSize, offset + request.PageSize < rows.Count);
        _cache.Set(key, page, _options.CacheTtlSeconds);
        return Result<InsightPage<InsightRow>>.Ok(page);
    }
}
