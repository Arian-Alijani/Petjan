using Animora.Application.Abstractions;
using Animora.Domain.Results;

namespace Animora.Application.Security;

/// <summary>
/// Entitlement plane (what the tenant's licence includes). Separate from authorization (A-RULE 023).
/// A failure of any other category is coerced to <c>ENTITLEMENT_REQUIRED</c>.
/// Default: <see cref="AllowAllEntitlementGate"/>, replaced in E07.
/// </summary>
public interface IEntitlementGate
{
    ValueTask<Result> EvaluateAsync(object command, CommandContext context, CancellationToken ct);

    ValueTask<Result> EvaluateAsync(object query, QueryContext context, CancellationToken ct);
}
