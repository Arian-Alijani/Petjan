using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
namespace Animora.Server.Files;

internal sealed record ValetBinding(string TenantId, string UserId, string DeviceId, string FileId, string Operation, long MaxBytes);
internal sealed class ValetKeyService(IDataProtectionProvider provider, TimeProvider clock)
{
    private readonly ITimeLimitedDataProtector _protector = provider.CreateProtector("Animora.Files.Valet.v1").ToTimeLimitedDataProtector();
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);
    public string Issue(ValetBinding binding)
    {
        var expires = clock.GetUtcNow() + Lifetime;
        return _protector.Protect(JsonSerializer.Serialize(new Grant(1, binding.TenantId, binding.UserId,
            binding.DeviceId, binding.FileId, binding.Operation, binding.MaxBytes, expires.ToUnixTimeSeconds())), expires);
    }
    public bool Validate(string? key, ValetBinding binding)
    {
        if (string.IsNullOrEmpty(key) || key.Length > 4096) return false;
        try
        {
            var grant = JsonSerializer.Deserialize<Grant>(_protector.Unprotect(key, out _));
            return grant is { v: 1 } && grant.tid == binding.TenantId && grant.uid == binding.UserId
                && grant.did == binding.DeviceId && grant.fid == binding.FileId && grant.op == binding.Operation
                && grant.max == binding.MaxBytes && grant.exp > clock.GetUtcNow().ToUnixTimeSeconds();
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or FormatException or ArgumentException) { return false; }
    }
    private sealed record Grant(int v, string tid, string uid, string did, string fid, string op, long max, long exp);
}
