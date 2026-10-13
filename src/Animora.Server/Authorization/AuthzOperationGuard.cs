using Animora.Server.Hosting;
using Microsoft.Extensions.Options;

namespace Animora.Server.Authorization;

/// <summary>
/// IMP-06: the authorization pipeline's own database calls (membership, device, tenant, entitlement) run inside a
/// per-operation budget (<c>Server:Authz:OperationTimeoutSeconds</c>). One slow query can no longer pin a request +
/// pooled connection for the whole 30 s Npgsql command timeout; it is cancelled at the budget and surfaces as a typed,
/// retryable <c>503 SERVER_BUSY</c> (see <see cref="AuthzOperationTimeoutException"/>).
/// A client abort still propagates as the caller's own <see cref="OperationCanceledException"/>.
/// </summary>
internal sealed class AuthzOperationGuard(IOptions<ServerOptions> options)
{
    private readonly TimeSpan _budget = TimeSpan.FromSeconds(Math.Max(1, options.Value.Authz.OperationTimeoutSeconds));

    /// <summary>Runs <paramref name="operation"/> with the per-operation budget linked to <paramref name="ct"/>.</summary>
    public async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(operation);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(_budget);
        try
        {
            return await operation(budget.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && budget.IsCancellationRequested)
        {
            throw new AuthzOperationTimeoutException();
        }
    }

    /// <summary><see cref="RunAsync{T}(Func{CancellationToken, Task{T}}, CancellationToken)"/> for <see cref="ValueTask{TResult}"/> operations.</summary>
    public Task<T> RunAsync<T>(Func<CancellationToken, ValueTask<T>> operation, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return RunAsync(token => operation(token).AsTask(), ct);
    }
}

/// <summary>An authorization-pipeline store call exceeded its per-operation budget; mapped to a typed 503 + Retry-After.</summary>
internal sealed class AuthzOperationTimeoutException() : Exception("Authorization lookup exceeded its per-operation budget.");
