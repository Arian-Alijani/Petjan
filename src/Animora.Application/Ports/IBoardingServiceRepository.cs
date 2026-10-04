using Animora.Domain.Boarding;

namespace Animora.Application.Ports;

/// <summary>Write-side access to the boarding-service catalogue (<see cref="BoardingServiceDef"/>).</summary>
public interface IBoardingServiceRepository
{
    Task<BoardingServiceDef?> FindAsync(string id, CancellationToken ct = default);

    void Add(BoardingServiceDef entity);
}
