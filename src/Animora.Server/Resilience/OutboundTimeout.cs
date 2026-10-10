namespace Animora.Server.Resilience;

internal sealed class OutboundTimeout(TimeProvider clock)
{
    public static readonly TimeSpan Default = TimeSpan.FromSeconds(10);
    public async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct, TimeSpan? timeout = null)
    {
        using var budget = new CancellationTokenSource(timeout ?? Default, clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, budget.Token);
        // WaitAsync also bounds an uncooperative consumer; consumers must still honor cancellation
        // to avoid late side effects. Built-in hint delivery does honor its cancellation token.
        return await work(linked.Token).WaitAsync(linked.Token).ConfigureAwait(false);
    }
}
