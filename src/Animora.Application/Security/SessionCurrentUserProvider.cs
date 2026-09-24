using Animora.Application.Abstractions;
using Animora.Application.Correlation;
using Animora.Application.Ports;
using Animora.Application.Validation;
using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Domain.Time;

namespace Animora.Application.Security;

/// <summary>
/// Real security context (B03b): tenant/device from the local installation, <c>UserId</c> from the live
/// local session. No active session → <c>SESSION_EXPIRED</c>; a locked session → <c>PERMISSION_DENIED</c>.
/// Nothing dispatches as a user without an authenticated, unlocked, unexpired session.
/// </summary>
public sealed class SessionCurrentUserProvider(
    ILocalInstallationIdentity installation,
    ISessionService sessions,
    SessionContextCache cache,
    ICorrelationContext correlation,
    OperationIdFactory operationIds,
    IClock clock) : ICurrentUserProvider
{
    public async Task<Result<CommandContext>> BuildCommandContextAsync(string? operationId, string? dependsOn,
        long? expectedVersion, CancellationToken ct)
    {
        if (InvalidOperationId(operationId) is { } bad) return Result<CommandContext>.Fail(bad.Error, bad.Detail);

        var snapshot = cache.Current;
        if (snapshot is null)
        {
            var restored = await sessions.RestoreAsync(ct).ConfigureAwait(false);
            snapshot = restored.IsSuccess ? restored.Value : null;
            cache.Set(snapshot);
        }

        var user = RequireActive(snapshot);
        if (user.IsFailure) return Result<CommandContext>.Fail(user.Error, user.Detail);
        return Result<CommandContext>.Ok(NewContext(user.Value!, operationId, dependsOn, expectedVersion));
    }

    public Result<QueryContext> BuildQueryContext()
    {
        var user = RequireActive(cache.Current);
        return user.IsFailure
            ? Result<QueryContext>.Fail(user.Error, user.Detail)
            : Result<QueryContext>.Ok(new QueryContext(installation.TenantId, user.Value!, CurrentCorrelationId()));
    }

    public Result<CommandContext> BuildAnonymousCommandContext(string? operationId = null)
    {
        if (InvalidOperationId(operationId) is { } bad) return Result<CommandContext>.Fail(bad.Error, bad.Detail);
        return Result<CommandContext>.Ok(NewContext(ICurrentUserProvider.AnonymousUserId, operationId, null, null));
    }

    public QueryContext BuildAnonymousQueryContext() =>
        new(installation.TenantId, ICurrentUserProvider.AnonymousUserId, CurrentCorrelationId());

    private Result<string> RequireActive(SessionSnapshot? snapshot)
    {
        if (snapshot?.UserId is not { Length: > 0 } userId
            || snapshot.ExpiresAtUtc is not { } expires || clock.UtcNow >= expires)
            return Result<string>.Fail(ErrorCodes.SessionExpired);
        return snapshot.IsLocked
            ? Result<string>.Fail(ErrorCodes.PermissionDenied, "Session is locked.")
            : Result<string>.Ok(userId);
    }

    private CommandContext NewContext(string userId, string? operationId, string? dependsOn, long? expectedVersion) => new(
        installation.TenantId,
        userId,
        installation.DeviceId,
        operationId ?? operationIds.New(),
        CurrentCorrelationId(),
        CausationId: null,
        DependsOnOperationId: dependsOn,
        ExpectedAggregateVersion: expectedVersion,
        CreatedAtUtc: clock.UtcNow);

    private static Result? InvalidOperationId(string? operationId) =>
        operationId is not null && Rules.UlidFormat(operationId, nameof(operationId)) is { } bad
            ? Result.Fail(ErrorCodes.ValidationFailed, ValidationFailure.FormatDetail([bad]))
            : null;

    private string CurrentCorrelationId() => correlation.CurrentId ?? IdGenerator.NewId();
}
