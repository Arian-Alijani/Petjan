namespace Animora.Server.Resilience;

internal sealed class OutboundTimeout(TimeProvider clock)
{
    public static readonly TimeSpan Default = TimeSpan.FromSeconds(10);
    public async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct, TimeSpan? timeout = null)
    {
        using var budget = new CancellationTokenSource(timeout ?? Default, clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, budget.Token);
        // Await cancellation unwind before reusing a scoped DbContext or updating the lease.
        // Consumers must honor cancellation; the built-in hint and all database calls do so.
        return await work(linked.Token).ConfigureAwait(false);
    }
}
