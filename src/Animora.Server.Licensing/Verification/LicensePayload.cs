using System.Text.RegularExpressions;
namespace Animora.Server.Licensing.Verification;

public sealed record LicensePayload(int V, string LicenseId, string TenantId, string TenantCode,
    string SubscriptionId, string PlanCode, int SeatLimit, int MaxDevices, string InstallationIdHash,
    string DeviceId, long Sequence, DateTimeOffset IssuedUtc, DateTimeOffset NotBeforeUtc,
    DateTimeOffset ExpiresUtc, DateTimeOffset OfflineGraceUntilUtc, string[] Features)
{
    public static bool IsInstallationHash(string? value) => value is not null && Regex.IsMatch(value, "^[a-f0-9]{64}$", RegexOptions.CultureInvariant);
    internal bool IsValid() => V == 1 && IsId(LicenseId) && IsId(TenantId) && IsId(SubscriptionId) && IsId(DeviceId)
        && !string.IsNullOrWhiteSpace(TenantCode) && !string.IsNullOrWhiteSpace(PlanCode)
        && SeatLimit > 0 && MaxDevices > 0 && Sequence > 0 && IsInstallationHash(InstallationIdHash)
        && IssuedUtc.Offset == TimeSpan.Zero && NotBeforeUtc.Offset == TimeSpan.Zero && ExpiresUtc.Offset == TimeSpan.Zero
        && OfflineGraceUntilUtc.Offset == TimeSpan.Zero && IssuedUtc != default && NotBeforeUtc <= IssuedUtc
        && ExpiresUtc >= IssuedUtc && OfflineGraceUntilUtc >= ExpiresUtc && Features is not null
        && Features.Length <= 64 && Features.All(x => x is not null && x.Length is > 0 and <= 64);
    private static bool IsId(string? value) => value is not null && Regex.IsMatch(value, "^[0-9A-HJKMNP-TV-Z]{26}$", RegexOptions.CultureInvariant);
}
