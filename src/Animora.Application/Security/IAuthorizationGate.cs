using Animora.Application.Abstractions;
using Animora.Domain.Results;

namespace Animora.Application.Security;

/// <summary>
/// Authorization plane (who may do what). Separate from the entitlement plane — the two are never
/// merged (A-RULE 023). A failure of any other category is coerced to <c>PERMISSION_DENIED</c>.
/// Default: <see cref="AllowAllAuthorizationGate"/>, replaced in B04.
/// </summary>
public interface IAuthorizationGate
{
    ValueTask<Result> EvaluateAsync(object command, CommandContext context, CancellationToken ct);

    ValueTask<Result> EvaluateAsync(object query, QueryContext context, CancellationToken ct);
}
