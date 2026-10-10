using System.Collections.Concurrent;
using Animora.Server.Hosting;
namespace Animora.Server.Workers;

internal sealed class WorkerHeartbeat(TimeProvider clock)
{
    private sealed record Entry(DateTimeOffset LastSuccessUtc, TimeSpan Period);
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    public void Register(string name, TimeSpan period) => _entries.TryAdd(name, new(clock.GetUtcNow(), period));
    public void Success(string name) => _entries.AddOrUpdate(name, _ => new(clock.GetUtcNow(), TimeSpan.FromSeconds(1)),
        (_, entry) => entry with { LastSuccessUtc = clock.GetUtcNow() });
    public void Period(string name, TimeSpan period) => _entries.AddOrUpdate(name, _ => new(clock.GetUtcNow(), period),
        (_, entry) => entry with { Period = period });
    public IReadOnlyList<string> StaleNames() => _entries.Where(e => clock.GetUtcNow() - e.Value.LastSuccessUtc > e.Value.Period * 5)
        .Select(e => e.Key).OrderBy(x => x, StringComparer.Ordinal).ToArray();
}
internal sealed class WorkersReadinessProbe(WorkerHeartbeat heartbeat) : IReadinessProbe
{
    public string Name => "workers";
    public bool IsRequired => false;
    public Task<ReadinessResult> CheckAsync(CancellationToken ct) => Task.FromResult(new ReadinessResult(heartbeat.StaleNames().Count == 0));
}
