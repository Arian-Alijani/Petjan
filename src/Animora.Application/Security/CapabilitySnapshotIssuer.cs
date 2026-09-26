using Animora.Application.Ports;
using Animora.Domain.Identity;
using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Domain.Time;

namespace Animora.Application.Security;

/// <summary>
/// Issues the device-bound offline capability snapshot at successful authentication (B04b). Effective codes are the
/// tenant's <b>stored</b> role definition + membership extras − membership denies (denies win), filtered to declared
/// codes. A missing stored role fails closed — the default-role fallback is never used for issuance.
/// Writes go through <see cref="ICapabilityStore"/> and join the caller's active unit-of-work transaction.
/// </summary>
public sealed class CapabilitySnapshotIssuer(
    IRoleDefinitionRepository roles,
    ICapabilityStore store,
    ICapabilitySnapshotSigner signer,
    IClock clock)
{
    public async Task<Result<StoredCapabilitySnapshot>> IssueAsync(User user, StaffMembership membership,
        string deviceId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(membership);
        if (string.IsNullOrWhiteSpace(deviceId))
            return Result<StoredCapabilitySnapshot>.Fail(ErrorCodes.CapabilityNotGranted, "device id required");
        if (!membership.IsActive || !string.Equals(membership.TenantId, user.TenantId, StringComparison.Ordinal)
            || !string.Equals(membership.UserId, user.Id, StringComparison.Ordinal))
            return Result<StoredCapabilitySnapshot>.Fail(ErrorCodes.CapabilityNotGranted, "membership does not match user");
        if (string.IsNullOrEmpty(user.SecurityStamp))
            return Result<StoredCapabilitySnapshot>.Fail(ErrorCodes.CapabilityNotGranted, "user has no security stamp");

        var role = await roles.FindByRoleAsync(membership.TenantId, membership.Role, ct).ConfigureAwait(false);
        if (role is null)
            return Result<StoredCapabilitySnapshot>.Fail(ErrorCodes.CapabilityNotGranted, "stored role definition missing");

        var codes = new SortedSet<string>(role.PermissionCodes, StringComparer.Ordinal);
        codes.UnionWith(membership.ExtraPermissionCodes);
        codes.ExceptWith(membership.DeniedPermissionCodes); // denies win (fail closed)
        codes.RemoveWhere(c => !PermissionCodes.Exists(c));

        var issued = clock.UtcNow.ToUniversalTime();
        var expiry = CapabilitySnapshot.BuildExpiry(codes, issued);
        // Empty set (or only zero-window codes) ⇒ absolute expiry == issuance ⇒ already expired: grants nothing.
        var absolute = expiry.Count == 0 ? issued : expiry.Values.Max();
        if (absolute < issued) absolute = issued;

        var unsigned = new StoredCapabilitySnapshot(
            Id: IdGenerator.NewId(clock.UtcNow),
            TenantId: membership.TenantId,
            UserId: user.Id,
            DeviceId: deviceId,
            PermissionsJson: CapabilitySnapshotCodec.WritePermissions(codes),
            IssuedAtUtc: issued,
            PerCapabilityExpiryJson: CapabilitySnapshotCodec.WriteExpiry(expiry),
            AbsoluteExpiryUtc: absolute,
            SnapshotSignature: string.Empty,
            SecurityStamp: user.SecurityStamp,
            RevokedAtUtc: null);

        var signature = signer.Sign(unsigned);
        if (signature.IsFailure) return Result<StoredCapabilitySnapshot>.Fail(signature.Error, signature.Detail);
        var signed = unsigned with { SnapshotSignature = signature.Value! };

        var replaced = await store.ReplaceAsync(signed, ct).ConfigureAwait(false);
        return replaced.IsFailure
            ? Result<StoredCapabilitySnapshot>.Fail(replaced.Error, replaced.Detail)
            : Result<StoredCapabilitySnapshot>.Ok(signed);
    }
}
