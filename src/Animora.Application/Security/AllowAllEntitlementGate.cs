using Animora.Application.Abstractions;
using Animora.Domain.Results;

namespace Animora.Application.Security;

/// <summary>Temporary allow-all entitlement plane. <b>Replaced by E07.</b></summary>
public sealed class AllowAllEntitlementGate : IEntitlementGate
{
    public ValueTask<Result> EvaluateAsync(object command, CommandContext context, CancellationToken ct) =>
        ValueTask.FromResult(Result.Ok());

    public ValueTask<Result> EvaluateAsync(object query, QueryContext context, CancellationToken ct) =>
        ValueTask.FromResult(Result.Ok());
}
