using System.Globalization;
using System.Text;
using Animora.Application.Commands;
using Animora.Server.Licensing.Verification;
namespace Animora.Server.Licensing.Issuance;

public static class LicenseCanonicalPayload
{
    public static IReadOnlyDictionary<string, object> Fields(LicensePayload p) => new Dictionary<string, object>(StringComparer.Ordinal)
    {
        ["v"] = p.V, ["licenseId"] = p.LicenseId, ["tenantId"] = p.TenantId, ["tenantCode"] = p.TenantCode,
        ["subscriptionId"] = p.SubscriptionId, ["planCode"] = p.PlanCode, ["seatLimit"] = p.SeatLimit,
        ["maxDevices"] = p.MaxDevices, ["installationIdHash"] = p.InstallationIdHash, ["deviceId"] = p.DeviceId,
        ["sequence"] = p.Sequence, ["issuedUtc"] = Utc(p.IssuedUtc), ["notBeforeUtc"] = Utc(p.NotBeforeUtc),
        ["expiresUtc"] = Utc(p.ExpiresUtc), ["offlineGraceUntilUtc"] = Utc(p.OfflineGraceUntilUtc), ["features"] = p.Features,
    };
    public static byte[] Serialize(LicensePayload payload) => Encoding.UTF8.GetBytes(CommandFingerprint.CanonicalJson(Fields(payload)));
    private static string Utc(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
}
