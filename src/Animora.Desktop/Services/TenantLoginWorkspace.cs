using Animora.Client.Services.Identity;
using Animora.Contracts.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Animora.Desktop.Services;

public sealed record TenantLoginServices(IAuthenticationService Authentication, IOnlineAuthClient OnlineAuth);

/// <summary>Owns an unactivated, isolated login attempt; only successful login transfers it to the shell.</summary>
public interface ITenantLoginWorkspace
{
    Task<TenantLoginServices> PrepareAsync(string clinicCode, CancellationToken ct);
    Task CommitAsync();
    Task ResetAsync();
}

public sealed class TenantLoginWorkspace(Func<string, IServiceProvider> create,
    Func<IServiceProvider, CancellationToken, Task<bool>> initialize,
    Func<IServiceProvider, Task> activate) : ITenantLoginWorkspace, IAsyncDisposable
{
    private IServiceProvider? _pending;

    public async Task<TenantLoginServices> PrepareAsync(string clinicCode, CancellationToken ct)
    {
        await ResetAsync();
        var candidate = create(clinicCode);
        _pending = candidate;
        try
        {
            if (!await initialize(candidate, ct)) throw new InvalidOperationException("Tenant store unavailable.");
            return new(candidate.GetRequiredService<IAuthenticationService>(), candidate.GetRequiredService<IOnlineAuthClient>());
        }
        catch
        {
            await ResetAsync();
            throw;
        }
    }

    public async Task CommitAsync()
    {
        var candidate = _pending ?? throw new InvalidOperationException("No tenant workspace prepared.");
        _pending = null; // transfer ownership before disposing the login provider (which owns this service)
        await activate(candidate);
    }

    public async Task ResetAsync()
    {
        var candidate = _pending;
        _pending = null;
        if (candidate is IAsyncDisposable disposable) await disposable.DisposeAsync();
        else if (candidate is IDisposable syncDisposable) syncDisposable.Dispose();
    }

    public ValueTask DisposeAsync() => new(ResetAsync());
}
