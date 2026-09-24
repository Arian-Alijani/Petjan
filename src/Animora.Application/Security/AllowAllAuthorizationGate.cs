using Animora.Application.Abstractions;
using Animora.Domain.Results;

namespace Animora.Application.Security;

/// <summary>Temporary allow-all authorization plane. <b>Replaced by B04.</b></summary>
public sealed class AllowAllAuthorizationGate : IAuthorizationGate
{
    public ValueTask<Result> EvaluateAsync(object command, CommandContext context, CancellationToken ct) =>
        ValueTask.FromResult(Result.Ok());

    public ValueTask<Result> EvaluateAsync(object query, QueryContext context, CancellationToken ct) =>
        ValueTask.FromResult(Result.Ok());
}
