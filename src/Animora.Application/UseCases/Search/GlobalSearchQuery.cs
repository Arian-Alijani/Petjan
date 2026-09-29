using Animora.Application.Abstractions;
using Animora.Application.Text;
using Animora.Domain.Results;

namespace Animora.Application.UseCases.Search;

public sealed record GlobalSearchRow(string Source, string Id, string Title, string Subtitle);
public interface IGlobalSearchQueries
{
    Task<IReadOnlyList<GlobalSearchRow>> SearchAsync(string tenantId, string term, int limit, CancellationToken ct);
}

public sealed record GlobalSearchQuery(string Term, int Limit = 8) : IQuery<IReadOnlyList<GlobalSearchRow>>;

public sealed class GlobalSearchQueryHandler(IGlobalSearchQueries queries)
    : IQueryHandler<GlobalSearchQuery, IReadOnlyList<GlobalSearchRow>>
{
    public async Task<Result<IReadOnlyList<GlobalSearchRow>>> HandleAsync(
        GlobalSearchQuery query, QueryContext context, CancellationToken ct)
    {
        var normalized = PersianText.Normalize(query.Term);
        if (normalized.Length < 2) return Result<IReadOnlyList<GlobalSearchRow>>.Ok([]);
        return Result<IReadOnlyList<GlobalSearchRow>>.Ok(
            await queries.SearchAsync(context.TenantId, normalized, Math.Clamp(query.Limit, 1, 30), ct));
    }
}

public static class SearchRegistration
{
    public static Animora.Application.Dispatch.ApplicationRegistrationBuilder AddSearchUseCases(
        this Animora.Application.Dispatch.ApplicationRegistrationBuilder builder) => builder
        .AddQuery<GlobalSearchQuery, IReadOnlyList<GlobalSearchRow>, GlobalSearchQueryHandler>();
}
