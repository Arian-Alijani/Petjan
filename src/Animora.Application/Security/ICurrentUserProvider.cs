using Animora.Application.Abstractions;
using Animora.Domain.Results;

namespace Animora.Application.Security;

/// <summary>Builds the ambient-identity contexts handed to the dispatcher (B03b: session-sourced).</summary>
public interface ICurrentUserProvider
{
    /// <summary>User id carried by pre-authentication contexts (sign-in, first-run, unlock, session query).</summary>
    public const string AnonymousUserId = "U-ANONYMOUS";

    /// <summary>
    /// Builds a <see cref="CommandContext"/> for the signed-in user. A null <paramref name="operationId"/> is
    /// minted by <c>OperationIdFactory</c> (A-RULE 005); a supplied one must be a ULID. Fails with
    /// <c>SESSION_EXPIRED</c> when no session is active.
    /// </summary>
    Task<Result<CommandContext>> BuildCommandContextAsync(string? operationId, string? dependsOn,
        long? expectedVersion, CancellationToken ct);

    /// <summary>Query context for the signed-in user; <c>SESSION_EXPIRED</c> when no session is active.</summary>
    Result<QueryContext> BuildQueryContext();

    /// <summary>
    /// Context for the identity commands that run before/without an unlocked session
    /// (<c>SignIn</c>, <c>CompleteFirstRunSetup</c>, <c>UnlockSession</c>). Carries <see cref="AnonymousUserId"/>.
    /// </summary>
    Result<CommandContext> BuildAnonymousCommandContext(string? operationId = null);

    /// <summary>Query context for <c>CurrentSessionQuery</c> before sign-in.</summary>
    QueryContext BuildAnonymousQueryContext();
}
