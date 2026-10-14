using System.Text;
using Animora.Contracts.Diagnostics;
using Animora.Application.Security;
using Animora.Domain.Results;
using Animora.Infrastructure.Local.Security;

namespace Animora.Infrastructure.Local.Storage;

/// <summary>
/// Pins a workspace to the authenticated server tenant in its own protected secret store. A clinic
/// code reassigned on the server cannot silently reuse another clinic's database. Mismatch preserves
/// the original files and fails closed rather than wiping valuable offline changes.
/// </summary>
public sealed class TenantSessionBinding(ISecretStore secrets, IClientDiagnosticsLog diag) : ISessionTenantBinding
{
    private const string LogCat = "identity.tenant-bind";
    private const string Key = "server-tenant-binding";
    private readonly object _gate = new();
    private int _active;
    public bool CanSync => Volatile.Read(ref _active) == 1;

    public Result Bind(string serverTenantId)
    {
        if (string.IsNullOrWhiteSpace(serverTenantId))
        {
            diag.Warn(LogCat, "Bind DENIED: server tenant missing (SESSION_EXPIRED).");
            return Result.Fail(ErrorCodes.SessionExpired);
        }
        lock (_gate)
        {
            var current = secrets.TryRead(Key);
            if (current.IsSuccess)
            {
                var pinned = Encoding.UTF8.GetString(current.Value!);
                if (string.Equals(pinned, serverTenantId, StringComparison.Ordinal))
                {
                    if (diag.IsDebugEnabled) diag.Debug(LogCat, $"Bind MATCH tenant={serverTenantId} canSync={CanSync}");
                    return Result.Ok();
                }
                diag.Warn(LogCat, $"Bind MISMATCH pinnedTenant={pinned} serverTenant={serverTenantId}; store_tenant_mismatch, existing offline files preserved.");
                return Result.Fail(ErrorCodes.Unauthorized, "store_tenant_mismatch");
            }
            if (current.Error != ErrorCodes.NotFound)
            {
                diag.Warn(LogCat, $"Bind READ_FAILED code={current.Error.Code}");
                return current.ToResult();
            }
            var result = secrets.Write(Key, Encoding.UTF8.GetBytes(serverTenantId));
            if (result.IsSuccess) diag.Info(LogCat, $"Bind PINNED tenant={serverTenantId}");
            else diag.Warn(LogCat, $"Bind WRITE_FAILED tenant={serverTenantId} code={result.Error.Code}");
            return result;
        }
    }

    public bool Matches(string serverTenantId)
    {
        lock (_gate)
        {
            var current = secrets.TryRead(Key);
            var matches = current.IsSuccess && !string.IsNullOrWhiteSpace(serverTenantId)
                && string.Equals(Encoding.UTF8.GetString(current.Value!), serverTenantId, StringComparison.Ordinal);
            if (!matches && diag.IsDebugEnabled)
                diag.Debug(LogCat, $"Matches=false serverTenant={serverTenantId} pinReadable={current.IsSuccess} canSync={CanSync}");
            return matches;
        }
    }

    public void Activate()
    {
        Volatile.Write(ref _active, 1);
        diag.Info(LogCat, "Sync binding ACTIVATED canSync=true");
    }

    public void Suspend()
    {
        Volatile.Write(ref _active, 0);
        diag.Info(LogCat, "Sync binding SUSPENDED canSync=false; local directory reads are independent of this flag.");
    }
}
