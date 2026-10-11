using System.Text.Json;
using Animora.Contracts.Wire.Licensing;
using Animora.Domain.Results;
using Animora.Infrastructure.Local.Security;
using Animora.Server.Licensing.Verification;

namespace Animora.Client.Services.Identity;

/// <summary>
/// The licence verification keys (<c>GET /licensing/keys</c>, public halves only) persisted through the OS keystore seam so a cold
/// offline start can still verify the sealed licence. Keys only ever grow/rotate by whole-set replacement from the server; reading never
/// throws. Maps the wire key (base64 DER) to the verification-half <see cref="Animora.Server.Licensing.Verification.LicensePublicKey"/> (byte[] DER).
/// </summary>
public sealed class LicenseKeyCache(ISecretStore secrets)
{
    private const string Name = "license-verification-keys";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly object _gate = new();
    private ILicenseKeySet? _cached;

    /// <summary>The cached key set, or <see langword="null"/> when none was ever stored.</summary>
    public ILicenseKeySet? Get()
    {
        lock (_gate)
        {
            if (_cached is not null) return _cached;
            var read = secrets.TryRead(Name);
            if (read.IsFailure || read.Value is not { Length: > 0 } bytes) return null;
            try
            {
                var wire = JsonSerializer.Deserialize<LicensePublicKeySet>(bytes, Json);
                return _cached = wire is null ? null : ToKeySet(wire);
            }
            catch (Exception ex) when (ex is JsonException or FormatException)
            {
                return null; // corrupt cache: behave as "never stored"
            }
        }
    }

    /// <summary>Replaces the cache with <paramref name="keys"/> (already fetched from the server).</summary>
    public Result Store(LicensePublicKeySet keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        ILicenseKeySet set;
        try
        {
            set = ToKeySet(keys);
        }
        catch (FormatException)
        {
            return Result.Fail(ErrorCodes.LicenseInvalid, "license_keys_malformed");
        }

        var written = secrets.Write(Name, JsonSerializer.SerializeToUtf8Bytes(keys, Json));
        if (written.IsFailure) return written;
        lock (_gate) _cached = set;
        return Result.Ok();
    }

    /// <summary>Maps the wire key set to the verification key set (Base64-decoding each DER key).</summary>
    public static ILicenseKeySet ToKeySet(LicensePublicKeySet wire)
    {
        ArgumentNullException.ThrowIfNull(wire);
        return new LicenseKeySet(wire.Keys
            .Select(k => new Animora.Server.Licensing.Verification.LicensePublicKey(
                k.KeyId, Convert.FromBase64String(k.PublicKeyDerBase64), k.NotBeforeUtc, k.NotAfterUtc))
            .ToArray());
    }
}
