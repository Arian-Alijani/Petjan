using Animora.Application.Security;
using Animora.Domain.Results;

namespace Animora.Sync.Custom;

/// <summary>Offline default <see cref="ISessionStore"/>: nobody is signed in online. Replaced by the OS-protected store when online identity is registered.</summary>
internal sealed class NullSessionStore : ISessionStore
{
    public Task<Result<ClientSession?>> ReadAsync(CancellationToken ct = default) => Task.FromResult(Result<ClientSession?>.Ok(null));

    public Task<Result> WriteAsync(ClientSession session, CancellationToken ct = default) => Task.FromResult(Result.Ok());

    public Task<Result> ClearAsync(CancellationToken ct = default) => Task.FromResult(Result.Ok());
}
