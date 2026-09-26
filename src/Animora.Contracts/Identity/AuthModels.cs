namespace Animora.Contracts.Identity;

/// <summary>Result of a sign-in / first-run attempt. <see cref="ErrorCode"/> is a stable code, not UI text.</summary>
public sealed record AuthOutcome(
    bool Succeeded,
    string? ErrorCode,
    string? DisplayName);

/// <summary>One-time owner setup input.</summary>
public sealed record FirstRunRequest(
    string ClinicName,
    string OwnerFullName,
    string Username,
    string Password);
