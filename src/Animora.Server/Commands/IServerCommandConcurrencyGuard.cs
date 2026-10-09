using Animora.Domain.Results;
namespace Animora.Server.Commands;

/// <summary>Fences shared scheduling resources inside intake's retrying transaction, before handler reads.</summary>
internal interface IServerCommandConcurrencyGuard
{
    Task<Result> PrepareAsync(object command, CancellationToken ct = default);
}
