using Animora.Contracts.Wire.Auth;
using Animora.Domain.Identity;
using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Domain.Time;
using Animora.Persistence.PostgreSql.Identity;
using Microsoft.AspNetCore.Identity;

namespace Animora.Server.Auth;

/// <summary>
/// The authentication plane (S04): sign-in, refresh-token rotation with reuse detection, sign-out and device
/// registration. <b>Authentication only</b> — authorization policies and the request tenant-context middleware
/// are S05. Order is always: resolve tenant → bind it → tenant-filtered user lookup. Every security action
/// writes an append-only audit row in the same transaction as its state change; audit detail never contains a
/// password, token or hash. Failures are <see cref="Result"/> values, never exceptions.
/// </summary>
internal sealed class LoginService(
    ITenantDirectory tenants,
    AuthTenantContext tenantContext,
    IIdentityRepository repository,
    RefreshTokenService refreshTokens,
    ISecurityAuditWriter audit,
    IPasswordHasher<ServerIdentityUser> hasher,
    IAccessTokenIssuer accessTokens,
    IClock clock)
{
    private const int MaxTenantCode = 32;
    private const int MaxUserName = 128;
    private const int MaxPassword = 1024;
    private const int MaxInstallationHash = 128;
    private const int MaxDeviceName = 128;
    private const int MaxPlatform = 64;
    private const int MaxRefreshToken = 512;

    private static string? s_dummyHash;

    /// <summary>Signs a user in on a device. Wrong tenant / user / password are indistinguishable.</summary>
    public async Task<Result<LoginResponse>> LoginAsync(LoginRequest? request, string correlationId, CancellationToken ct = default)
    {
        if (request is null
            || IsBlankOrLonger(request.TenantCode, MaxTenantCode)
            || IsBlankOrLonger(request.UserName, MaxUserName)
            || IsBlankOrLonger(request.Password, MaxPassword)
            || IsBlankOrLonger(request.DeviceInstallationIdHash, MaxInstallationHash))
        {
            return Failure<LoginResponse>(ErrorCodes.ValidationFailed);
        }

        Result<TenantDirectoryEntry> tenantResult = await tenants.FindByCodeAsync(request.TenantCode, ct).ConfigureAwait(false);
        if (tenantResult.IsFailure)
        {
            BurnPasswordHash(request.Password);
            return Failure<LoginResponse>(ErrorCodes.CredentialsInvalid);
        }

        TenantDirectoryEntry tenant = tenantResult.Value!; // non-null on success
        tenantContext.Bind(tenant.Id);

        if (tenant.Status != TenantStatus.Active)
        {
            await AuditAsync(tenant.Id, SecurityAuditActions.LoginFailure, correlationId, null, null, "tenant_not_active", ct).ConfigureAwait(false);
            return Failure<LoginResponse>(ErrorCodes.TenantSuspended);
        }

        string userName = request.UserName.Trim().ToLowerInvariant();
        User? user = await repository.FindUserByNameAsync(userName, ct).ConfigureAwait(false);
        if (user is null)
        {
            BurnPasswordHash(request.Password);
            await AuditAsync(tenant.Id, SecurityAuditActions.LoginFailure, correlationId, null, null, "unknown_user", ct).ConfigureAwait(false);
            return Failure<LoginResponse>(ErrorCodes.CredentialsInvalid);
        }

        // A disabled account reveals itself only to someone who proves the password (no enumeration), and its
        // failures never touch the lockout counters (a failed attempt must not flip Disabled to Locked).
        if (user.Status == UserStatus.Disabled)
        {
            if (!VerifyPassword(user, request.Password))
            {
                await AuditAsync(tenant.Id, SecurityAuditActions.LoginFailure, correlationId, user.Id, null, "bad_password", ct).ConfigureAwait(false);
                return Failure<LoginResponse>(ErrorCodes.CredentialsInvalid);
            }

            await AuditAsync(tenant.Id, SecurityAuditActions.LoginFailure, correlationId, user.Id, null, "user_disabled", ct).ConfigureAwait(false);
            return Failure<LoginResponse>(ErrorCodes.UserDisabled);
        }

        if (user.IsLockedOut(clock.UtcNow))
        {
            await AuditAsync(tenant.Id, SecurityAuditActions.LoginLocked, correlationId, user.Id, null, "locked_out", ct).ConfigureAwait(false);
            return Failure<LoginResponse>(ErrorCodes.UserLockedOut);
        }

        if (!VerifyPassword(user, request.Password))
        {
            bool lockedNow = user.RegisterFailedAttempt(clock);
            await AuditAsync(
                tenant.Id,
                lockedNow ? SecurityAuditActions.LoginLocked : SecurityAuditActions.LoginFailure,
                correlationId,
                user.Id,
                null,
                lockedNow ? "lockout_threshold_reached" : "bad_password",
                ct).ConfigureAwait(false);
            return Failure<LoginResponse>(lockedNow ? ErrorCodes.UserLockedOut : ErrorCodes.CredentialsInvalid);
        }

        StaffMembership? membership = await repository.FindActiveMembershipAsync(user.Id, ct).ConfigureAwait(false);
        if (membership is null)
        {
            await AuditAsync(tenant.Id, SecurityAuditActions.LoginFailure, correlationId, user.Id, null, "membership_not_active", ct).ConfigureAwait(false);
            return Failure<LoginResponse>(ErrorCodes.MembershipNotActive);
        }

        string installationHash = request.DeviceInstallationIdHash.Trim();
        Device? device = await repository.FindDeviceByInstallationAsync(user.Id, installationHash, ct).ConfigureAwait(false);
        if (device is { Status: DeviceStatus.Revoked })
        {
            await AuditAsync(tenant.Id, SecurityAuditActions.DeviceRevokedContact, correlationId, user.Id, device.Id, "device_revoked", ct).ConfigureAwait(false);
            return Failure<LoginResponse>(ErrorCodes.DeviceRevoked);
        }

        bool deviceIsNew = device is null;
        if (device is null)
        {
            // Unknown installation: bind it. Staff devices start Pending and need a manager's approval (S05);
            // a tenant Manager's own device is auto-approved so the owner is never locked out of their own
            // tenant — otherwise the first/only manager device stays Pending forever with no active device able
            // to approve it, which blocks capability issuance and every entitlement-gated feature.
            device = Device.Register(tenant.Id, user.Id, "Unnamed device", installationHash, "unknown", clock);
            if (membership.Role == StaffRole.Manager)
            {
                device.Approve();
            }

            repository.AddDevice(device);
        }

        user.RegisterSuccessfulLogin(clock);
        device.RecordContact(clock);

        (string refreshWire, _) = refreshTokens.Issue(tenant.Id, user.Id, device.Id);
        IssuedAccessToken access = accessTokens.Issue(new AccessTokenClaims(user.Id, tenant.Id, device.Id, user.SecurityStamp, membership.Role.ToString()));

        Result saved = await AuditAsync(
            tenant.Id,
            SecurityAuditActions.LoginSuccess,
            correlationId,
            user.Id,
            device.Id,
            deviceIsNew ? "device_registered" : null,
            ct).ConfigureAwait(false);

        return saved.IsFailure
            ? Failure<LoginResponse>(saved.Error)
            : Result<LoginResponse>.Ok(new LoginResponse(
                access.Token,
                access.ExpiresInSeconds,
                refreshWire,
                user.Id,
                tenant.Id,
                device.Id,
                membership.Role.ToString(),
                user.MustChangePassword));
    }

    /// <summary>Rotates a refresh token. A replayed (already rotated) token revokes its whole family.</summary>
    public async Task<Result<LoginResponse>> RefreshAsync(RefreshRequest? request, string correlationId, CancellationToken ct = default)
    {
        string? presented = request?.RefreshToken;
        if (IsBlankOrLonger(presented, MaxRefreshToken))
        {
            return Failure<LoginResponse>(ErrorCodes.SessionExpired);
        }

        string? tenantId = RefreshTokenService.TryReadTenantId(presented);
        if (tenantId is null)
        {
            return Failure<LoginResponse>(ErrorCodes.SessionExpired);
        }

        Result<TenantDirectoryEntry> tenantResult = await tenants.FindByIdAsync(tenantId, ct).ConfigureAwait(false);
        if (tenantResult.IsFailure)
        {
            return Failure<LoginResponse>(ErrorCodes.SessionExpired);
        }

        TenantDirectoryEntry tenant = tenantResult.Value!; // non-null on success
        tenantContext.Bind(tenant.Id);

        if (tenant.Status != TenantStatus.Active)
        {
            return Failure<LoginResponse>(ErrorCodes.TenantSuspended);
        }

        // Validate the account BEFORE rotating, so a transient refusal (e.g. lockout) never burns the token.
        RefreshToken? row = await refreshTokens.FindAsync(presented!, ct).ConfigureAwait(false); // presented checked above
        if (row is not null && row.IsActive(clock.UtcNow))
        {
            Result<LoginResponse>? refusal = await PreRotationChecksAsync(tenant.Id, row, correlationId, ct).ConfigureAwait(false);
            if (refusal is { } denied)
            {
                return denied;
            }
        }

        RotationOutcome outcome = await refreshTokens.RotateAsync(presented!, ct).ConfigureAwait(false);
        switch (outcome.Status)
        {
            case RotationStatus.Reused:
                {
                    RefreshToken previous = outcome.Previous!; // set whenever Status is Reused
                    User? victim = await repository.FindUserByIdAsync(previous.UserId, ct).ConfigureAwait(false);
                    victim?.RotateSecurityStamp(); // invalidates every outstanding access token of the user
                    await AuditAsync(tenant.Id, SecurityAuditActions.RefreshReuseDetected, correlationId, previous.UserId, previous.DeviceId, "token_reuse", ct).ConfigureAwait(false);
                    return Failure<LoginResponse>(ErrorCodes.SessionExpired);
                }

            case RotationStatus.Rotated:
                break;

            default:
                return Failure<LoginResponse>(ErrorCodes.SessionExpired);
        }

        RefreshToken current = outcome.Current!; // set whenever Status is Rotated
        User? user = await repository.FindUserByIdAsync(current.UserId, ct).ConfigureAwait(false);
        StaffMembership? membership = user is null ? null : await repository.FindActiveMembershipAsync(user.Id, ct).ConfigureAwait(false);
        Device? device = await repository.FindDeviceByIdAsync(current.DeviceId, ct).ConfigureAwait(false);
        if (user is null || membership is null || device is null)
        {
            await refreshTokens.RevokeFamilyAsync(current.FamilyId, ct).ConfigureAwait(false);
            return Failure<LoginResponse>(ErrorCodes.SessionExpired);
        }

        device.RecordContact(clock);
        IssuedAccessToken access = accessTokens.Issue(new AccessTokenClaims(user.Id, tenant.Id, device.Id, user.SecurityStamp, membership.Role.ToString()));

        Result saved = await AuditAsync(tenant.Id, SecurityAuditActions.RefreshSuccess, correlationId, user.Id, device.Id, null, ct).ConfigureAwait(false);
        return saved.IsFailure
            ? Failure<LoginResponse>(saved.Error)
            : Result<LoginResponse>.Ok(new LoginResponse(
                access.Token,
                access.ExpiresInSeconds,
                outcome.WireToken!, // set whenever Status is Rotated
                user.Id,
                tenant.Id,
                device.Id,
                membership.Role.ToString(),
                user.MustChangePassword));
    }

    /// <summary>Signs the caller out of its device: every open refresh token of that user + device is revoked.</summary>
    public async Task<Result> LogoutAsync(AuthenticatedCaller caller, string correlationId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);

        Result<User> valid = await ValidateCallerAsync(caller, ct).ConfigureAwait(false);
        if (valid.IsFailure)
        {
            return Result.Fail(valid.Error);
        }

        await refreshTokens.RevokeDeviceTokensAsync(caller.UserId, caller.DeviceId, ct).ConfigureAwait(false);
        return await AuditAsync(caller.TenantId, SecurityAuditActions.Logout, correlationId, caller.UserId, caller.DeviceId, null, ct).ConfigureAwait(false);
    }

    /// <summary>Registers (or re-reads) the caller's device installation; new devices start <c>Pending</c>.</summary>
    public async Task<Result<DeviceRegisterResponse>> RegisterDeviceAsync(
        AuthenticatedCaller caller,
        DeviceRegisterRequest? request,
        string correlationId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);

        if (request is null
            || IsBlankOrLonger(request.DeviceName, MaxDeviceName)
            || IsBlankOrLonger(request.InstallationIdHash, MaxInstallationHash)
            || IsBlankOrLonger(request.Platform, MaxPlatform))
        {
            return Failure<DeviceRegisterResponse>(ErrorCodes.ValidationFailed);
        }

        Result<User> valid = await ValidateCallerAsync(caller, ct).ConfigureAwait(false);
        if (valid.IsFailure)
        {
            return Failure<DeviceRegisterResponse>(valid.Error);
        }

        string installationHash = request.InstallationIdHash.Trim();
        Device? existing = await repository.FindDeviceByInstallationAsync(caller.UserId, installationHash, ct).ConfigureAwait(false);
        if (existing is { Status: DeviceStatus.Revoked })
        {
            await AuditAsync(caller.TenantId, SecurityAuditActions.DeviceRevokedContact, correlationId, caller.UserId, existing.Id, "device_revoked", ct).ConfigureAwait(false);
            return Failure<DeviceRegisterResponse>(ErrorCodes.DeviceRevoked);
        }

        // A tenant Manager's own device is auto-approved (owner bootstrap); staff devices stay Pending for a
        // manager to approve. This also self-heals an already-Pending manager device so an owner whose tenant
        // was provisioned before this fix is no longer locked out of their own tenant.
        StaffMembership? membership = await repository.FindActiveMembershipAsync(caller.UserId, ct).ConfigureAwait(false);
        bool isManager = membership is { Role: StaffRole.Manager };

        if (existing is not null)
        {
            // The first contact for this installation is login, which can only store placeholder metadata
            // ("Unnamed device" / "unknown") because the login request carries none; refresh it now from the
            // authenticated register call so the manager's device roster shows the real machine and platform.
            bool described = existing.DescribeInstallation(request.DeviceName, request.Platform.Trim());
            bool autoApproved = false;
            if (isManager && existing.Status == DeviceStatus.Pending)
            {
                existing.Approve();
                autoApproved = true;
            }

            if (described || autoApproved)
            {
                // Writing the audit row commits the staged device changes (description and/or approval) in one unit of work.
                Result updated = await AuditAsync(
                    caller.TenantId,
                    SecurityAuditActions.DeviceRegistered,
                    correlationId,
                    caller.UserId,
                    existing.Id,
                    autoApproved ? "owner_auto_approved" : "device_described",
                    ct).ConfigureAwait(false);
                if (updated.IsFailure)
                {
                    return Failure<DeviceRegisterResponse>(updated.Error);
                }
            }

            return Result<DeviceRegisterResponse>.Ok(new DeviceRegisterResponse(existing.Id, existing.Status.ToString()));
        }

        Device device = Device.Register(caller.TenantId, caller.UserId, request.DeviceName, installationHash, request.Platform.Trim(), clock);
        if (isManager)
        {
            device.Approve();
        }

        repository.AddDevice(device);

        Result saved = await AuditAsync(caller.TenantId, SecurityAuditActions.DeviceRegistered, correlationId, caller.UserId, device.Id, null, ct).ConfigureAwait(false);
        return saved.IsFailure
            ? Failure<DeviceRegisterResponse>(saved.Error)
            : Result<DeviceRegisterResponse>.Ok(new DeviceRegisterResponse(device.Id, device.Status.ToString()));
    }

    private async Task<Result<LoginResponse>?> PreRotationChecksAsync(string tenantId, RefreshToken row, string correlationId, CancellationToken ct)
    {
        User? user = await repository.FindUserByIdAsync(row.UserId, ct).ConfigureAwait(false);
        if (user is null)
        {
            await refreshTokens.RevokeFamilyAsync(row.FamilyId, ct).ConfigureAwait(false);
            return Failure<LoginResponse>(ErrorCodes.SessionExpired);
        }

        if (user.Status == UserStatus.Disabled)
        {
            await refreshTokens.RevokeFamilyAsync(row.FamilyId, ct).ConfigureAwait(false);
            await AuditAsync(tenantId, SecurityAuditActions.RefreshFailure, correlationId, user.Id, row.DeviceId, "user_disabled", ct).ConfigureAwait(false);
            return Failure<LoginResponse>(ErrorCodes.UserDisabled);
        }

        if (user.IsLockedOut(clock.UtcNow))
        {
            // Transient: leave the token intact so the client can refresh once the lockout ends.
            await AuditAsync(tenantId, SecurityAuditActions.RefreshFailure, correlationId, user.Id, row.DeviceId, "locked_out", ct).ConfigureAwait(false);
            return Failure<LoginResponse>(ErrorCodes.UserLockedOut);
        }

        Device? device = await repository.FindDeviceByIdAsync(row.DeviceId, ct).ConfigureAwait(false);
        if (device is null || device.Status == DeviceStatus.Revoked)
        {
            await refreshTokens.RevokeFamilyAsync(row.FamilyId, ct).ConfigureAwait(false);
            await AuditAsync(tenantId, SecurityAuditActions.DeviceRevokedContact, correlationId, user.Id, row.DeviceId, "device_revoked", ct).ConfigureAwait(false);
            return Failure<LoginResponse>(ErrorCodes.DeviceRevoked);
        }

        StaffMembership? membership = await repository.FindActiveMembershipAsync(user.Id, ct).ConfigureAwait(false);
        if (membership is null)
        {
            await refreshTokens.RevokeFamilyAsync(row.FamilyId, ct).ConfigureAwait(false);
            await AuditAsync(tenantId, SecurityAuditActions.RefreshFailure, correlationId, user.Id, row.DeviceId, "membership_not_active", ct).ConfigureAwait(false);
            return Failure<LoginResponse>(ErrorCodes.MembershipNotActive);
        }

        return null;
    }

    private async Task<Result<User>> ValidateCallerAsync(AuthenticatedCaller caller, CancellationToken ct)
    {
        Result<TenantDirectoryEntry> tenantResult = await tenants.FindByIdAsync(caller.TenantId, ct).ConfigureAwait(false);
        if (tenantResult.IsFailure)
        {
            return Result<User>.Fail(ErrorCodes.SessionExpired);
        }

        TenantDirectoryEntry tenant = tenantResult.Value!; // non-null on success
        if (tenant.Status != TenantStatus.Active)
        {
            return Result<User>.Fail(ErrorCodes.TenantSuspended);
        }

        tenantContext.Bind(tenant.Id);

        User? user = await repository.FindUserByIdAsync(caller.UserId, ct).ConfigureAwait(false);
        if (user is null || !string.Equals(user.SecurityStamp, caller.SecurityStamp, StringComparison.Ordinal))
        {
            return Result<User>.Fail(ErrorCodes.SessionExpired);
        }

        return user.Status == UserStatus.Disabled
            ? Result<User>.Fail(ErrorCodes.UserDisabled)
            : Result<User>.Ok(user);
    }

    private bool VerifyPassword(User user, string password)
    {
        if (string.IsNullOrEmpty(user.PasswordHash))
        {
            BurnPasswordHash(password);
            return false;
        }

        PasswordVerificationResult result = hasher.VerifyHashedPassword(ServerUserStore.ToAdapter(user), user.PasswordHash, password);
        return result is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded;
    }

    /// <summary>Spends one password-hash verification so a miss costs about the same as a hit (timing equalization).</summary>
    private void BurnPasswordHash(string password)
    {
        var probe = new ServerIdentityUser { Id = "dummy", UserName = "dummy", NormalizedUserName = "DUMMY" };
        string dummy = s_dummyHash ??= hasher.HashPassword(probe, "Dummy-password-0");
        _ = hasher.VerifyHashedPassword(probe, dummy, password);
    }

    private Task<Result> AuditAsync(
        string tenantId,
        string action,
        string correlationId,
        string? userId,
        string? deviceId,
        string? reason,
        CancellationToken ct)
    {
        bool success = action is SecurityAuditActions.LoginSuccess
            or SecurityAuditActions.RefreshSuccess
            or SecurityAuditActions.Logout
            or SecurityAuditActions.DeviceRegistered;

        string? detail = reason is null ? null : SecurityAuditDetail.Create(("reason", reason));
        SecurityAuditEntry entry = SecurityAuditEntry.Create(
            IdGenerator.NewId(clock.UtcNow),
            tenantId,
            clock.UtcNow,
            SecurityAuditActions.Category,
            action,
            success ? SecurityAuditActions.OutcomeSuccess : SecurityAuditActions.OutcomeFailure,
            Truncate(correlationId, 64),
            userId,
            deviceId,
            detail);

        return audit.WriteAsync(entry, ct);
    }

    private static Result<T> Failure<T>(ErrorCode error) => Result<T>.Fail(error);

    private static bool IsBlankOrLonger(string? value, int max) => string.IsNullOrWhiteSpace(value) || value.Length > max;

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
