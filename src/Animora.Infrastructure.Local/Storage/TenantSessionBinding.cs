using System.Text;
using Animora.Application.Security;
using Animora.Domain.Results;
using Animora.Infrastructure.Local.Security;

namespace Animora.Infrastructure.Local.Storage;

/// <summary>
/// Pins a workspace to the authenticated server tenant in its own protected secret store. A clinic
/// code reassigned on the server cannot silently reuse another clinic's database. Mismatch preserves
/// the original files and fails closed rather than wiping valuable offline changes.
/// </summary>
public sealed class TenantSessionBinding(ISecretStore secrets) : ISessionTenantBinding
{
    private const string Key = "server-tenant-binding";
    private readonly object _gate = new();
    private int _active;
    public bool CanSync => Volatile.Read(ref _active) == 1;

    public Result Bind(string serverTenantId)
    {
        if (string.IsNullOrWhiteSpace(serverTenantId)) return Result.Fail(ErrorCodes.SessionExpired);
        lock (_gate)
        {
            var current = secrets.TryRead(Key);
            if (current.IsSuccess)
                return string.Equals(Encoding.UTF8.GetString(current.Value!), serverTenantId, StringComparison.Ordinal)
                    ? Result.Ok() : Result.Fail(ErrorCodes.Unauthorized, "store_tenant_mismatch");
            if (current.Error != ErrorCodes.NotFound) return current.ToResult();
            return secrets.Write(Key, Encoding.UTF8.GetBytes(serverTenantId));
        }
    }

    public bool Matches(string serverTenantId)
    {
        lock (_gate)
        {
            var current = secrets.TryRead(Key);
            return current.IsSuccess && !string.IsNullOrWhiteSpace(serverTenantId)
                && string.Equals(Encoding.UTF8.GetString(current.Value!), serverTenantId, StringComparison.Ordinal);
        }
    }

    public void Activate() => Volatile.Write(ref _active, 1);
    public void Suspend() => Volatile.Write(ref _active, 0);
}
