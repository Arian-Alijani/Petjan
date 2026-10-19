using Animora.Application.Security;
using Animora.Domain.Results;

namespace Animora.LiveTest.Client;

/// <summary>
/// In-memory <see cref="ISessionStore"/> for one virtual client. The real desktop persists the session in an
/// OS-protected store; a load-test client only needs the bearer pair to live as long as the process.
/// </summary>
internal sealed class MemorySessionStore : ISessionStore
{
    private readonly object _gate = new();
    private ClientSession? _session;

    public Task<Result<ClientSession?>> ReadAsync(CancellationToken ct = default)
    {
        lock (_gate) return Task.FromResult(Result<ClientSession?>.Ok(_session));
    }

    public Task<Result> WriteAsync(ClientSession session, CancellationToken ct = default)
    {
        lock (_gate) _session = session;
        return Task.FromResult(Result.Ok());
    }

    public Task<Result> ClearAsync(CancellationToken ct = default)
    {
        lock (_gate) _session = null;
        return Task.FromResult(Result.Ok());
    }
}
