using System.Security.Cryptography;
using System.Text.Json;
using Animora.Domain.Results;
namespace Animora.Server.Licensing.Verification;

public static class LicenseVerifier
{
    public static Result<LicensePayload> Verify(ParsedLicense license, ILicenseKeySet keys)
    {
        var key = keys.Find(license.KeyId);
        if (key is null) return Result<LicensePayload>.Fail(ErrorCodes.LicenseKeyUnknown);
        try
        {
            using var ec = ECDsa.Create();
            ec.ImportSubjectPublicKeyInfo(key.PublicKeyDer, out var read);
            if (read != key.PublicKeyDer.Length || ec.KeySize != 256 || license.Signature.Length != 64
                || !ec.VerifyData(license.PayloadBytes, license.Signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
                return Result<LicensePayload>.Fail(ErrorCodes.LicenseInvalid);
            // Exact signed bytes, never re-canonicalized by verification. Reject duplicate JSON properties.
            using var document = JsonDocument.Parse(license.PayloadBytes);
            if (document.RootElement.ValueKind != JsonValueKind.Object || document.RootElement.EnumerateObject()
                .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase).Any(x => x.Count() != 1)) return Result<LicensePayload>.Fail(ErrorCodes.LicenseInvalid);
            var payload = JsonSerializer.Deserialize<LicensePayload>(license.PayloadBytes, JsonSerializerOptions.Web);
            if (payload is null || !payload.IsValid() || payload.IssuedUtc < key.NotBeforeUtc || key.NotAfterUtc is { } end && payload.IssuedUtc > end)
                return Result<LicensePayload>.Fail(ErrorCodes.LicenseInvalid);
            return Result<LicensePayload>.Ok(payload);
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or ArgumentException or NotSupportedException)
        { return Result<LicensePayload>.Fail(ErrorCodes.LicenseInvalid); }
    }
}
