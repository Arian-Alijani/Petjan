using System.Globalization;
using System.Text;
using System.Text.Json;
using Animora.Domain.Identity;

namespace Animora.Server.Signing;

/// <summary>
/// The one deterministic serialization of a <see cref="CapabilitySnapshot"/> that is signed and verified.
/// Fixed field order, capability keys sorted ordinally, every timestamp UTC in round-trip (<c>o</c>) format, role
/// as its name. The <c>Signature</c> field is never part of the signed bytes.
/// </summary>
internal static class CapabilitySnapshotCanonicalizer
{
    /// <summary>Canonical UTF-8 JSON of <paramref name="snapshot"/> (without its signature).</summary>
    public static byte[] Canonicalize(CapabilitySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("tenantId", snapshot.TenantId);
            writer.WriteString("userId", snapshot.UserId);
            writer.WriteString("deviceId", snapshot.DeviceId);
            writer.WriteString("role", snapshot.Role.ToString());
            writer.WriteString("issuedUtc", Format(snapshot.IssuedUtc));
            writer.WriteString("snapshotExpiryUtc", Format(snapshot.SnapshotExpiryUtc));
            writer.WriteString("securityStamp", snapshot.SecurityStamp);
            writer.WriteStartObject("capabilityExpiry");
            foreach (KeyValuePair<string, DateTimeOffset> item in snapshot.CapabilityExpiry.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                writer.WriteString(item.Key, Format(item.Value));
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    /// <summary>The canonical payload as a string (what the wire response carries).</summary>
    public static string CanonicalizeToString(CapabilitySnapshot snapshot) => Encoding.UTF8.GetString(Canonicalize(snapshot));

    private static string Format(DateTimeOffset value) => value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
}
