using Animora.Application.Abstractions;
using Animora.Application.Commands;
using Animora.Application.Security;
using Animora.Domain.Results;
using Animora.Server.Authorization;

namespace Animora.Server.Commands;

/// <summary>Server profile reuses the S05 stored subject, never the desktop session/capability provider.</summary>
internal sealed class ServerPipelineAuthorizationGate(IRequestAuthorizationState state, ICommandAuthorizer authorizer) : IAuthorizationGate
{
    public async ValueTask<Result> EvaluateAsync(object command, CommandContext context, CancellationToken ct = default)
    {
        if (state.Membership is not { } caller || state.Subject is null)
            return Result.Fail(ErrorCodes.PermissionDenied);
        if (caller.TenantId != context.TenantId || caller.UserId != context.UserId || caller.DeviceId != context.DeviceId)
            return Result.Fail(ErrorCodes.TenantMismatch);
        return command is IJournaledCommand journaled
            ? await authorizer.AuthorizeAsync(journaled.CommandTypeName, state.Subject, ct).ConfigureAwait(false)
            : Result.Fail(ErrorCodes.PermissionUnknown);
    }

    public ValueTask<Result> EvaluateAsync(object query, QueryContext context, CancellationToken ct = default) =>
        ValueTask.FromResult(Result.Fail(ErrorCodes.PermissionUnknown));
}

internal sealed class ServerPipelineEntitlementGate(IServerEntitlementGate gate) : IEntitlementGate
{
    public ValueTask<Result> EvaluateAsync(object command, CommandContext context, CancellationToken ct = default) =>
        gate.EvaluateAsync(new TenantEntitlementContext(context.TenantId, context.UserId,
            (command as IJournaledCommand)?.CommandTypeName), ct);
    public ValueTask<Result> EvaluateAsync(object query, QueryContext context, CancellationToken ct = default) =>
        gate.EvaluateAsync(new TenantEntitlementContext(context.TenantId, context.UserId, null), ct);
}
