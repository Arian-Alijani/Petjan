namespace Animora.Contracts.Identity;

/// <summary>Result of a sign-in / first-run attempt. <see cref="ErrorCode"/> is a stable code, not UI text.</summary>
public sealed record AuthOutcome(
    bool Succeeded,
    string? ErrorCode,
    string? DisplayName);

/// <summary>
/// One-time owner setup input. <see cref="ServerUserId"/> is the central server's id for this owner when the setup
/// follows a verified online sign-in (D01) — the local owner then carries the same id as the server user.
/// </summary>
public sealed record FirstRunRequest(
    string ClinicName,
    string OwnerFullName,
    string Username,
    string Password,
    string? ServerUserId = null);

/// <summary>
/// The identity the central server has just authenticated, handed to the local store so it can mirror the user
/// (TASK-1). <see cref="ServerRole"/> is the server role name (parsed to the local staff role by the service).
/// </summary>
public sealed record ServerIdentityMirrorRequest(
    string ServerUserId,
    string ServerTenantId,
    string Username,
    string Password,
    string ServerRole);
