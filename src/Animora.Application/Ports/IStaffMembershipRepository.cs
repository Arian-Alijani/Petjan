using Animora.Domain.Identity;

namespace Animora.Application.Ports;

public interface IStaffMembershipRepository
{
    Task<StaffMembership?> FindAsync(string id, CancellationToken ct = default);

    /// <summary>The user's active membership inside <paramref name="tenantId"/>, if any.</summary>
    Task<StaffMembership?> FindActiveAsync(string tenantId, string userId, CancellationToken ct = default);

    void Add(StaffMembership entity);
}
