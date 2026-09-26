using Animora.Application.Abstractions;
using Animora.Domain.Results;

namespace Animora.Application.Security;

/// <summary>
/// Authorization plane (who may do what). Separate from the entitlement plane — the two are never
/// merged (A-RULE 023). A failure of any other category is coerced to <c>PERMISSION_DENIED</c>.
/// Implemented by <see cref="PermissionAuthorizationGate"/> (B04a); there is no allow-all default.
/// </summary>
public interface IAuthorizationGate
{
    ValueTask<Result> EvaluateAsync(object command, CommandContext context, CancellationToken ct);

    ValueTask<Result> EvaluateAsync(object query, QueryContext context, CancellationToken ct);
}
