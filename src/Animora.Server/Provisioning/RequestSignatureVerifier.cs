using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Animora.Domain.Results;
using Animora.Domain.Time;
using Animora.Persistence.PostgreSql.Provisioning;
using Microsoft.AspNetCore.DataProtection;
namespace Animora.Server.Provisioning;

internal sealed class RequestSignatureVerifier(WebsiteClientStore clients, IDataProtectionProvider protection, ReplayWindow replay, IClock clock)
{
    public static string CanonicalRequest(string method, string path, string timestamp, ReadOnlySpan<byte> body) =>
        method.ToUpperInvariant() + "\n" + path + "\n" + timestamp + "\n" + Convert.ToHexStringLower(SHA256.HashData(body));
    public static bool VerifySignature(byte[]? secret, string method, string path, string timestamp, string signature,
        ReadOnlySpan<byte> body, DateTimeOffset now)
    {
        if (!long.TryParse(timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) || signature.Length != 64)
            return false;
        DateTimeOffset issued;
        try { issued = DateTimeOffset.FromUnixTimeSeconds(seconds); }
        catch (ArgumentOutOfRangeException) { return false; }
        if ((now - issued).Duration() > TimeSpan.FromMinutes(5)) return false;
        byte[] actual;
        try { actual = Convert.FromHexString(signature); } catch (FormatException) { return false; }
        var expected = HMACSHA256.HashData(secret ?? new byte[32], Encoding.UTF8.GetBytes(CanonicalRequest(method, path, timestamp, body)));
        return CryptographicOperations.FixedTimeEquals(actual, expected) && secret is not null;
    }
    public async Task<Result<string>> AuthenticateAsync(HttpContext http, byte[] body, CancellationToken ct = default)
    {
        var key = http.Request.Headers["X-Animora-Key"].ToString();
        var timestamp = http.Request.Headers["X-Animora-Timestamp"].ToString();
        var signature = http.Request.Headers["X-Animora-Signature"].ToString();
        var client = key.Length is > 0 and <= 32 ? await clients.FindAsync(key, ct).ConfigureAwait(false) : null;
        byte[]? secret = null;
        try
        {
            if (client is not null) secret = protection.CreateProtector("Animora.Provisioning.Client.v1").Unprotect(client.ProtectedSecret);
            var verified = VerifySignature(secret, http.Request.Method, http.Request.PathBase + http.Request.Path,
                timestamp, signature, body, clock.UtcNow);
            if (verified && client is not null && replay.TryAccept(key, signature, clock.UtcNow))
            { await clients.TouchAsync(client.Id, clock.UtcNow, ct).ConfigureAwait(false); return Result<string>.Ok(client.Id); }
        }
        catch (CryptographicException) { /* Same response as unknown/revoked key. */ }
        finally { if (secret is not null) CryptographicOperations.ZeroMemory(secret); }
        await clients.DeniedAsync(clock.UtcNow, ct).ConfigureAwait(false);
        return Result<string>.Fail(ErrorCodes.ProvisioningSignatureInvalid);
    }
}
