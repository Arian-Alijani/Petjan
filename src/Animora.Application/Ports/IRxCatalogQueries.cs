using Animora.Application.UseCases.Visits;

namespace Animora.Application.Ports;

public interface IRxCatalogQueries
{
    Task<IReadOnlyList<RxCatalogItemReadModel>> GetAsync(CancellationToken ct = default);
}
