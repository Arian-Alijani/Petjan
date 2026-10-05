using Animora.Domain.Boarding;

namespace Animora.Application.Ports;

/// <summary>Write-side access to the boarding rooms (<see cref="Room"/>).</summary>
public interface IRoomRepository
{
    Task<Room?> FindAsync(string id, CancellationToken ct = default);

    /// <summary>Whether a room other than <paramref name="exceptId"/> already uses <paramref name="code"/>.</summary>
    Task<bool> CodeExistsAsync(string code, string? exceptId, CancellationToken ct = default);

    void Add(Room entity);
}
