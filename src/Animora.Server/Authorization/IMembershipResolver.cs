using Animora.Domain.Identity;
using Animora.Domain.Results;

namespace Animora.Server.Authorization;

/// <summary>The facts of an accepted request, all read from the server store (never from token claims).</summary>
/// <param name="TenantId">Tenant of the user's active membership.</param>
/// <param name="Role">Stored role of the active membership.</param>
/// <param name="UserId">User ULID.</param>
/// <param name="DeviceId">Device ULID.</param>
/// <param name="DeviceActive"><see langword="true"/> when the device is <c>Active</c> (not merely <c>Pending</c>).</param>
internal sealed record ResolvedMembership(string TenantId, StaffRole Role, string UserId, string DeviceId, bool DeviceActive);

/// <summary>Binds an authenticated caller to its tenant strictly from the stored active membership.</summary>
internal interface IMembershipResolver
{
    /// <summary>
    /// Resolves and <b>binds</b> the tenant for the current request, or fails closed with a reused
    /// <c>ErrorCodes.*</c> value. Not cached across requests: revocation / suspension is effective immediately.
    /// </summary>
    /// <param name="userId">Token <c>sub</c>.</param>
    /// <param name="deviceId">Token <c>did</c>.</param>
    /// <param name="securityStamp">Token <c>sst</c>; must equal the stored stamp.</param>
    /// <param name="ct">Cancellation.</param>
    Task<Result<ResolvedMembership>> ResolveAsync(string userId, string deviceId, string securityStamp, CancellationToken ct = default);
}
