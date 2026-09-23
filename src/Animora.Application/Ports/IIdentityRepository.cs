using Animora.Domain.Identity;

namespace Animora.Application.Ports;

public interface IIdentityRepository
{
    Task<User?> FindAsync(string id, CancellationToken ct = default);
    Task<User> RequireAsync(string id, CancellationToken ct = default);
    void Add(User entity);
    void AddRange(IEnumerable<User> entities);
}
