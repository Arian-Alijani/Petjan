using Animora.Domain.Results;
namespace Animora.Server.Licensing.Verification;

/// <summary>Version-one cache. Implementations must atomically merge before writing; high-water and sequence never decrease.</summary>
public sealed record SealedEntitlement(string LicenseToken, long HighWaterUnixMs, long HighestSequenceSeen,
    string TenantId, string InstallationIdHash, long WrittenUnixMs);
public interface ISealedEntitlementStore
{
    Task<Result<SealedEntitlement?>> ReadAsync(CancellationToken ct = default);
    Task<Result> WriteAsync(SealedEntitlement entitlement, CancellationToken ct = default);
}
public static class SealedEntitlementMerge
{
    public static Result<SealedEntitlement> Merge(SealedEntitlement? existing, SealedEntitlement incoming)
    {
        if (incoming.HighestSequenceSeen < 1 || string.IsNullOrWhiteSpace(incoming.TenantId) || !LicensePayload.IsInstallationHash(incoming.InstallationIdHash)
            || LicenseEnvelope.Parse(incoming.LicenseToken).IsFailure) return Result<SealedEntitlement>.Fail(ErrorCodes.LicenseInvalid);
        if (existing is null) return Result<SealedEntitlement>.Ok(incoming);
        if (existing.TenantId != incoming.TenantId || existing.InstallationIdHash != incoming.InstallationIdHash)
            return Result<SealedEntitlement>.Fail(ErrorCodes.LicenseDeviceMismatch);
        if (incoming.HighestSequenceSeen < existing.HighestSequenceSeen) return Result<SealedEntitlement>.Fail(ErrorCodes.LicenseInvalid, "sequence_rollback");
        if (incoming.HighestSequenceSeen == existing.HighestSequenceSeen && incoming.LicenseToken != existing.LicenseToken)
            return Result<SealedEntitlement>.Fail(ErrorCodes.LicenseInvalid, "sequence_token_conflict");
        return Result<SealedEntitlement>.Ok(incoming with { HighWaterUnixMs = Math.Max(existing.HighWaterUnixMs, incoming.HighWaterUnixMs),
            WrittenUnixMs = Math.Max(existing.WrittenUnixMs, incoming.WrittenUnixMs) });
    }
}
