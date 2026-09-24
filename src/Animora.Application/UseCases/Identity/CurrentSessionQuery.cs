using Animora.Application.Abstractions;
using Animora.Application.Security;
using Animora.Domain.Results;

namespace Animora.Application.UseCases.Identity;

/// <summary>Restores/reads the local session (startup, lock screen). Dispatched with an anonymous context.</summary>
public sealed record CurrentSessionQuery : IQuery<SessionSnapshot>;

public sealed class CurrentSessionQueryHandler(ISessionService sessions, SessionContextCache cache)
    : IQueryHandler<CurrentSessionQuery, SessionSnapshot>
{
    public async Task<Result<SessionSnapshot>> HandleAsync(CurrentSessionQuery query, QueryContext context,
        CancellationToken ct)
    {
        var restored = await sessions.RestoreAsync(ct).ConfigureAwait(false);
        cache.Set(restored.IsSuccess ? restored.Value : SessionSnapshot.SignedOut(isFirstRun: false));
        return restored;
    }
}
