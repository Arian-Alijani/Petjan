using System.Buffers;
using System.Text.Json;
using Animora.Domain.Results;

namespace Animora.Application.Security;

/// <summary>
/// Immutable, installation-local offline capability snapshot exactly as persisted (B04b, A-RULE 029).
/// Device-bound and signed: <see cref="SnapshotSignature"/> covers every other field (see
/// <see cref="ICapabilitySnapshotSigner"/>). It grants nothing by itself — <see cref="CapabilityEvaluator"/> decides.
/// </summary>
/// <param name="PermissionsJson">JSON array of permission codes (canonical: ordinal-sorted, distinct).</param>
/// <param name="PerCapabilityExpiryJson">JSON object <c>code → UTC ticks</c> (canonical: ordinal-sorted keys).</param>
/// <param name="RevokedAtUtc">Set once revoked; reads may survive, writes never do.</param>
public sealed record StoredCapabilitySnapshot(
    string Id,
    string TenantId,
    string UserId,
    string DeviceId,
    string PermissionsJson,
    DateTimeOffset IssuedAtUtc,
    string PerCapabilityExpiryJson,
    DateTimeOffset AbsoluteExpiryUtc,
    string SnapshotSignature,
    string SecurityStamp,
    DateTimeOffset? RevokedAtUtc);

/// <summary>
/// Local persistence of the single current snapshot per <c>(tenant, user, device)</c>. Writes participate in the
/// caller's active unit-of-work transaction when one exists; standalone calls are atomic on their own.
/// </summary>
public interface ICapabilityStore
{
    /// <summary>The current snapshot for exactly this triple; <see langword="null"/> value when none exists.</summary>
    Task<Result<StoredCapabilitySnapshot?>> GetAsync(string tenantId, string userId, string deviceId,
        CancellationToken ct = default);

    /// <summary>Atomically replaces the triple's current snapshot with <paramref name="snapshot"/> (already signed).</summary>
    Task<Result> ReplaceAsync(StoredCapabilitySnapshot snapshot, CancellationToken ct = default);

    /// <summary>
    /// Marks the triple's snapshot revoked and re-signs it atomically. Absent → success (nothing grants).
    /// A row failing verification is deleted and <c>CAPABILITY_SNAPSHOT_TAMPERED</c> returned — never re-signed.
    /// </summary>
    Task<Result> RevokeAsync(string tenantId, string userId, string deviceId, DateTimeOffset revokedAtUtc,
        CancellationToken ct = default);
}

/// <summary>
/// Application-owned signing port (implemented in Infrastructure.Local; key from the OS keystore). Expected
/// failures (keystore unavailable, malformed payload, mismatch) are <see cref="Result"/>s, not exceptions.
/// </summary>
public interface ICapabilitySnapshotSigner
{
    /// <summary>Base64 signature over the canonical form of every field except the signature itself.</summary>
    Result<string> Sign(StoredCapabilitySnapshot snapshot);

    /// <summary>Ok when the signature matches; <c>CAPABILITY_SNAPSHOT_TAMPERED</c> on mismatch/malformed payload.</summary>
    Result Verify(StoredCapabilitySnapshot snapshot);
}

/// <summary>Parsed, normalized capability payload of a snapshot.</summary>
public sealed record CapabilityPayload(
    IReadOnlyList<string> Permissions,
    IReadOnlyList<KeyValuePair<string, DateTimeOffset>> Expiry)
{
    /// <summary>Per-code expiry lookup (ordinal).</summary>
    public bool TryGetExpiry(string code, out DateTimeOffset expiry)
    {
        foreach (var pair in Expiry)
        {
            if (string.Equals(pair.Key, code, StringComparison.Ordinal))
            {
                expiry = pair.Value;
                return true;
            }
        }

        expiry = default;
        return false;
    }
}

/// <summary>
/// The single JSON codec and canonicalization shared by issuer, signer and evaluator. Parsing rejects malformed
/// JSON, empty/duplicate codes and a permission set that differs from the expiry key set.
/// </summary>
public static class CapabilitySnapshotCodec
{
    private const int CanonicalVersion = 1;

    /// <summary>Canonical <c>PermissionsJson</c>: ordinal-sorted, distinct.</summary>
    public static string WritePermissions(IEnumerable<string> codes)
    {
        var sorted = codes.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        return JsonSerializer.Serialize(sorted);
    }

    /// <summary>Canonical <c>PerCapabilityExpiryJson</c>: ordinal-sorted keys, UTC ticks values.</summary>
    public static string WriteExpiry(IReadOnlyDictionary<string, DateTimeOffset> expiry)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            foreach (var pair in expiry.OrderBy(p => p.Key, StringComparer.Ordinal))
                writer.WriteNumber(pair.Key, pair.Value.UtcTicks);
            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>Parses and normalizes both JSON columns; failure means the row is unusable (deny).</summary>
    public static Result<CapabilityPayload> Parse(string? permissionsJson, string? expiryJson)
    {
        if (string.IsNullOrWhiteSpace(permissionsJson) || string.IsNullOrWhiteSpace(expiryJson))
            return Malformed("empty capability payload");
        try
        {
            var permissions = new SortedSet<string>(StringComparer.Ordinal);
            using (var doc = JsonDocument.Parse(permissionsJson))
            {
                if (doc.RootElement.ValueKind != JsonValueKind.Array) return Malformed("permissions is not an array");
                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String) return Malformed("permission code is not a string");
                    var code = item.GetString();
                    if (string.IsNullOrWhiteSpace(code) || !permissions.Add(code))
                        return Malformed("empty or duplicate permission code");
                }
            }

            var expiry = new SortedDictionary<string, DateTimeOffset>(StringComparer.Ordinal);
            using (var doc = JsonDocument.Parse(expiryJson))
            {
                if (doc.RootElement.ValueKind != JsonValueKind.Object) return Malformed("expiry is not an object");
                foreach (var property in doc.RootElement.EnumerateObject())
                {
                    if (string.IsNullOrWhiteSpace(property.Name) || expiry.ContainsKey(property.Name))
                        return Malformed("empty or duplicate expiry code");
                    if (property.Value.ValueKind != JsonValueKind.Number || !property.Value.TryGetInt64(out var ticks)
                        || ticks < DateTimeOffset.MinValue.UtcTicks || ticks > DateTimeOffset.MaxValue.UtcTicks)
                        return Malformed("expiry is not UTC ticks");
                    expiry.Add(property.Name, new DateTimeOffset(ticks, TimeSpan.Zero));
                }
            }

            if (!permissions.SetEquals(expiry.Keys)) return Malformed("permission set differs from expiry keys");
            return Result<CapabilityPayload>.Ok(new CapabilityPayload(permissions.ToArray(), expiry.ToArray()));
        }
        catch (JsonException)
        {
            return Malformed("invalid JSON");
        }
    }

    /// <summary>
    /// The one deterministic UTF-8 canonical representation signed and verified: every security-relevant field
    /// (signature excluded), normalized payload, timestamps as UTC ticks, fixed field order.
    /// </summary>
    public static Result<byte[]> Canonicalize(StoredCapabilitySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var parsed = Parse(snapshot.PermissionsJson, snapshot.PerCapabilityExpiryJson);
        if (parsed.IsFailure) return Result<byte[]>.Fail(parsed.Error, parsed.Detail);
        var payload = parsed.Value!;

        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("v", CanonicalVersion);
            writer.WriteString("id", snapshot.Id);
            writer.WriteString("tenant", snapshot.TenantId);
            writer.WriteString("user", snapshot.UserId);
            writer.WriteString("device", snapshot.DeviceId);
            writer.WriteStartArray("permissions");
            foreach (var code in payload.Permissions) writer.WriteStringValue(code);
            writer.WriteEndArray();
            writer.WriteNumber("issued", snapshot.IssuedAtUtc.UtcTicks);
            writer.WriteStartArray("expiry");
            foreach (var pair in payload.Expiry)
            {
                writer.WriteStartArray();
                writer.WriteStringValue(pair.Key);
                writer.WriteNumberValue(pair.Value.UtcTicks);
                writer.WriteEndArray();
            }

            writer.WriteEndArray();
            writer.WriteNumber("absolute", snapshot.AbsoluteExpiryUtc.UtcTicks);
            writer.WriteString("stamp", snapshot.SecurityStamp);
            if (snapshot.RevokedAtUtc is { } revoked) writer.WriteNumber("revoked", revoked.UtcTicks);
            else writer.WriteNull("revoked");
            writer.WriteEndObject();
        }

        return Result<byte[]>.Ok(buffer.WrittenSpan.ToArray());
    }

    private static Result<CapabilityPayload> Malformed(string detail) =>
        Result<CapabilityPayload>.Fail(ErrorCodes.CapabilitySnapshotTampered, detail);
}
