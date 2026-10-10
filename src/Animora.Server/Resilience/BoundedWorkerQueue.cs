using System.Threading.Channels;
namespace Animora.Server.Resilience;

internal sealed class BoundedWorkerQueue<T>
{
    public const int Capacity = 64;
    public const int MaxConcurrentConsumers = 2;
    private readonly Channel<T> _channel = Channel.CreateBounded<T>(new BoundedChannelOptions(Capacity)
        { FullMode = BoundedChannelFullMode.Wait, SingleReader = false, SingleWriter = true, AllowSynchronousContinuations = false });
    public ValueTask WriteAsync(T value, CancellationToken ct) => _channel.Writer.WriteAsync(value, ct);
    public IAsyncEnumerable<T> ReadAllAsync(CancellationToken ct) => _channel.Reader.ReadAllAsync(ct);
    public void Complete() => _channel.Writer.TryComplete();
}
