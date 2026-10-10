namespace Animora.Contracts.Wire.Licensing;

public sealed record LicenseRequest(string InstallationIdHash, long? CurrentSequence = null);
public sealed record LicenseResponse(string Token, DateTimeOffset ExpiresUtc, DateTimeOffset OfflineGraceUntilUtc,
    long Sequence, DateTimeOffset ServerTimeUtc);
public sealed record LicenseStatusResponse(string GrantStatus, DateTimeOffset? ValidUntilUtc, string? PlanCode,
    int? SeatLimit, int? MaxDevices, int ActiveLicenses, DateTimeOffset ServerTimeUtc);
public sealed record LicensePublicKey(string KeyId, string Algorithm, string PublicKeyDerBase64,
    DateTimeOffset NotBeforeUtc, DateTimeOffset? NotAfterUtc);
public sealed record LicensePublicKeySet(IReadOnlyList<LicensePublicKey> Keys, DateTimeOffset ServerTimeUtc);
