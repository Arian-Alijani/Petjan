using Animora.Application.Abstractions;
using Animora.Domain.Results;

namespace Animora.Application.Security;

/// <summary>Builds the ambient-identity contexts handed to the dispatcher. Replaced by B03.</summary>
public interface ICurrentUserProvider
{
    /// <summary>
    /// Builds a <see cref="CommandContext"/>. A null <paramref name="operationId"/> is minted by
    /// <c>OperationIdFactory</c> (A-RULE 005); a supplied one must be a ULID.
    /// </summary>
    Task<Result<CommandContext>> BuildCommandContextAsync(string? operationId, string? dependsOn,
        long? expectedVersion, CancellationToken ct);

    Result<QueryContext> BuildQueryContext();
}
