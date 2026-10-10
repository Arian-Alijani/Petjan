using Animora.Domain.Results;
namespace Animora.Server.Licensing.Verification;

public sealed record ParsedLicense(byte[] PayloadBytes, byte[] Signature, string KeyId);
public static class LicenseEnvelope
{
    public const int MaxTokenLength = 4096;
    public static Result<ParsedLicense> Parse(string? token)
    {
        if (string.IsNullOrEmpty(token) || token.Length > MaxTokenLength) return Result<ParsedLicense>.Fail(ErrorCodes.LicenseInvalid);
        var parts = token.Split('.');
        if (parts.Length != 3 || parts[2].Length != 26 || parts[2].Any(c => !char.IsAsciiLetterOrDigit(c))
            || !Base64Url.TryDecode(parts[0], out var payload) || !Base64Url.TryDecode(parts[1], out var signature) || signature.Length != 64)
            return Result<ParsedLicense>.Fail(ErrorCodes.LicenseInvalid);
        return Result<ParsedLicense>.Ok(new(payload, signature, parts[2]));
    }
    public static string Format(ReadOnlySpan<byte> payload, ReadOnlySpan<byte> signature, string keyId) =>
        Base64Url.Encode(payload) + "." + Base64Url.Encode(signature) + "." + keyId;
}
