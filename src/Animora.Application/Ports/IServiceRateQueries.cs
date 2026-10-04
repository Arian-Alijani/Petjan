using Animora.Application.UseCases.Sales;

namespace Animora.Application.Ports;

/// <summary>Read side of the service-rate catalogue (clinic services + boarding add-on services).</summary>
public interface IServiceRateQueries
{
    /// <summary>Every non-deleted clinic service product and every boarding service, active or not.</summary>
    Task<IReadOnlyList<ServiceRateReadModel>> GetAsync(CancellationToken ct);

    /// <summary>The id of the seeded services category (<c>SRV</c>), else the first live category, else <see langword="null"/>.</summary>
    Task<string?> FindServiceCategoryIdAsync(CancellationToken ct);
}
