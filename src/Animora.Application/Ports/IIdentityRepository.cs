using Animora.Domain.Identity;

namespace Animora.Application.Ports;

public interface IIdentityRepository
{
    Task<User?> FindAsync(string id, CancellationToken ct = default);
    Task<User> RequireAsync(string id, CancellationToken ct = default);

    /// <summary>Tenant-scoped lookup by login name (normalized trim + lowercase invariant).</summary>
    Task<User?> FindByUsernameAsync(string tenantId, string userName, CancellationToken ct = default);

    /// <summary><see langword="true"/> once any user exists (first-run setup completed).</summary>
    Task<bool> AnyUserAsync(CancellationToken ct = default);

    Task<Tenant?> FindTenantAsync(string tenantId, CancellationToken ct = default);
    void Add(User entity);
    void AddRange(IEnumerable<User> entities);
}
