using Animora.Domain.Boarding;

namespace Animora.Application.Ports;

public interface IBoardingRepository
{
    Task<BoardingStay?> FindAsync(string id, CancellationToken ct = default);
    Task<BoardingStay> RequireAsync(string id, CancellationToken ct = default);
    void Add(BoardingStay entity);
    void AddRange(IEnumerable<BoardingStay> entities);
}
