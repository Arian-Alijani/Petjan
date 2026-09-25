using Animora.Application.Abstractions;
using Animora.Application.Commands;
using Animora.Application.Security;
using Animora.Domain.Results;

namespace Animora.Application.UseCases.Identity;

/// <summary>Locks the active session (manual or idle lock).</summary>
public sealed record LockSessionCommand : ICommand<Unit>, ISelfTransactedCommand;

public sealed class LockSessionCommandHandler(ISessionService sessions, SessionContextCache cache)
    : ICommandHandler<LockSessionCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(LockSessionCommand command, CommandContext context, CancellationToken ct)
    {
        var locked = await sessions.LockAsync(ct).ConfigureAwait(false);
        if (locked.IsFailure) return Result<Unit>.Fail(locked.Error, locked.Detail);
        cache.MarkLocked();
        return Result<Unit>.Ok(Unit.Value);
    }
}
