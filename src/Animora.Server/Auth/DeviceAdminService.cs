using Animora.Application.Security;
using Animora.Contracts.Wire.Auth;
using Animora.Domain.Identity;
using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Domain.Time;
using Animora.Persistence.PostgreSql.Identity;
using Animora.Persistence.PostgreSql.Security;
using Animora.Server.Authorization;
using Animora.Server.Signing;

namespace Animora.Server.Auth;

/// <summary>
/// Device approval / revocation, capability-snapshot issuance and the public key listing (S05). Callers are already
/// authenticated, tenant-bound and permission-checked by the pipeline; this service only applies the state change
/// and writes the audit row in the same transaction. Failures are <see cref="Result"/> values, never exceptions.
/// </summary>
internal sealed class DeviceAdminService(
    IIdentityRepository repository,
    ISecurityAuditWriter audit,
    ServerAuthorizationSubjectFactory subjects,
    ISnapshotSignerProvider signers,
    IClock clock)
{
    /// <summary>Audit action: a device was approved.</summary>
    public const string DeviceApprovedAction = "auth.device.approved";

    /// <summary>Audit action: a device was revoked.</summary>
    public const string DeviceRevokedAction = "auth.device.revoked";

    /// <summary>Audit action: a capability snapshot was issued.</summary>
    public const string CapabilitiesIssuedAction = "auth.capabilities.issued";

    /// <summary>Upper bound of any snapshot's lifetime.</summary>
    public static readonly TimeSpan MaxSnapshotLifetime = TimeSpan.FromDays(30);

    private const int MaxReason = 256;

    /// <summary>Approves a pending device in the caller's tenant.</summary>
    public async Task<Result<DeviceRegisterResponse>> ApproveAsync(
        ResolvedMembership actor,
        string deviceId,
        string correlationId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(actor);

        Device? device = await repository.FindDeviceByIdAsync(deviceId, ct).ConfigureAwait(false);
        if (device is null)
        {
            return Result<DeviceRegisterResponse>.Fail(ErrorCodes.NotFound);
        }

        if (device.Status == DeviceStatus.Revoked)
        {
            return Result<DeviceRegisterResponse>.Fail(ErrorCodes.DeviceRevoked);
        }

        device.Approve();
        Result saved = await AuditAsync(actor, DeviceApprovedAction, correlationId, device.Id, ct).ConfigureAwait(false);
        return saved.IsFailure
            ? Result<DeviceRegisterResponse>.Fail(saved.Error)
            : Result<DeviceRegisterResponse>.Ok(new DeviceRegisterResponse(device.Id, device.Status.ToString()));
    }

    /// <summary>
    /// Revokes a device and rotates its owner's security stamp, so every live access token of that user is
    /// invalid on its next request. Device, stamp and audit row commit in one unit of work.
    /// </summary>
    public async Task<Result<DeviceRegisterResponse>> RevokeAsync(
        ResolvedMembership actor,
        string deviceId,
        DeviceRevokeRequest? request,
        string correlationId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(actor);

        if (request is null || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > MaxReason)
        {
            return Result<DeviceRegisterResponse>.Fail(ErrorCodes.ValidationFailed);
        }

        Device? device = await repository.FindDeviceByIdAsync(deviceId, ct).ConfigureAwait(false);
        if (device is null)
        {
            return Result<DeviceRegisterResponse>.Fail(ErrorCodes.NotFound);
        }

        User? owner = await repository.FindUserByIdAsync(device.UserId, ct).ConfigureAwait(false);

        device.Revoke(request.Reason, clock);
        owner?.RotateSecurityStamp();

        Result saved = await AuditAsync(actor, DeviceRevokedAction, correlationId, device.Id, ct).ConfigureAwait(false);
        return saved.IsFailure
            ? Result<DeviceRegisterResponse>.Fail(saved.Error)
            : Result<DeviceRegisterResponse>.Ok(new DeviceRegisterResponse(device.Id, device.Status.ToString()));
    }

    /// <summary>Issues and signs the caller's offline capability snapshot.</summary>
    public async Task<Result<CapabilitySnapshotResponse>> IssueCapabilitiesAsync(
        ResolvedMembership actor,
        AuthenticatedCaller caller,
        string correlationId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(caller);

        IReadOnlySet<string> grants = await subjects.GrantsAsync(actor.TenantId, actor.UserId, actor.Role, ct).ConfigureAwait(false);
        ISnapshotSigner signer = await signers.GetAsync(ct).ConfigureAwait(false);

        DateTimeOffset issued = clock.UtcNow.ToUniversalTime();
        CapabilitySnapshot snapshot = BuildSnapshot(actor, caller.SecurityStamp, grants, issued);

        Result<string> signature = signer.Sign(CapabilitySnapshotCanonicalizer.Canonicalize(snapshot));
        if (signature.IsFailure)
        {
            return Result<CapabilitySnapshotResponse>.Fail(signature.Error);
        }

        Result saved = await AuditAsync(actor, CapabilitiesIssuedAction, correlationId, actor.DeviceId, ct).ConfigureAwait(false);
        return saved.IsFailure
            ? Result<CapabilitySnapshotResponse>.Fail(saved.Error)
            : Result<CapabilitySnapshotResponse>.Ok(new CapabilitySnapshotResponse(
                CapabilitySnapshotCanonicalizer.CanonicalizeToString(snapshot),
                signer.KeyId,
                signature.Value!)); // non-null on success
    }

    /// <summary>
    /// Pure snapshot construction: <b>Critical</b> codes (zero offline window) and non-catalog codes are omitted;
    /// the snapshot expires at the earliest granted capability expiry, never later than
    /// <see cref="MaxSnapshotLifetime"/> after issuance.
    /// </summary>
    public static CapabilitySnapshot BuildSnapshot(
        ResolvedMembership actor,
        string securityStamp,
        IEnumerable<string> grants,
        DateTimeOffset issuedUtc)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(grants);

        string[] offline = grants
            .Where(code => PermissionCodes.Exists(code) && OfflineCapabilityPolicy.WindowForCode(code) > TimeSpan.Zero)
            .ToArray();

        IReadOnlyDictionary<string, DateTimeOffset> expiry = CapabilitySnapshot.BuildExpiry(offline, issuedUtc);
        DateTimeOffset cap = issuedUtc + MaxSnapshotLifetime;
        DateTimeOffset snapshotExpiry = expiry.Count == 0 ? cap : expiry.Values.Min();
        if (snapshotExpiry > cap)
        {
            snapshotExpiry = cap;
        }

        return new CapabilitySnapshot(
            actor.TenantId,
            actor.UserId,
            actor.DeviceId,
            actor.Role,
            expiry,
            issuedUtc,
            snapshotExpiry,
            securityStamp,
            Signature: string.Empty); // the detached signature travels beside the payload, not inside it
    }

    /// <summary>Public halves of every snapshot key (retired ones included), newest first. No private material.</summary>
    public async Task<PublicKeySet> ListPublicKeysAsync(CancellationToken ct = default)
    {
        IReadOnlyList<SigningKey> rows = await signers.ListPublicAsync(SigningKey.Purposes.Snapshot, ct).ConfigureAwait(false);
        return new PublicKeySet(rows
            .Select(k => new PublicKeyInfo(k.KeyId, k.Algorithm, Convert.ToBase64String(k.PublicKeyDer), k.NotAfterUtc))
            .ToArray());
    }

    private Task<Result> AuditAsync(ResolvedMembership actor, string action, string correlationId, string deviceId, CancellationToken ct)
    {
        SecurityAuditEntry entry = SecurityAuditEntry.Create(
            IdGenerator.NewId(clock.UtcNow),
            actor.TenantId,
            clock.UtcNow,
            SecurityAuditActions.Category,
            action,
            SecurityAuditActions.OutcomeSuccess,
            correlationId.Length <= 64 ? correlationId : correlationId[..64],
            actor.UserId,
            deviceId);

        // Commits every change staged on the unit of work (device / stamp) together with the audit row.
        return audit.WriteAsync(entry, ct);
    }
}
