using Animora.Contracts.Diagnostics;
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
    IClock clock,
    IClientDiagnosticsLog diag) : ICurrentUserProvider
{
    private const string LogCat = "identity.scope";

    public async Task<Result<CommandContext>> BuildCommandContextAsync(string? operationId, string? dependsOn,
        long? expectedVersion, CancellationToken ct)
    {
        if (InvalidOperationId(operationId) is { } bad) return Result<CommandContext>.Fail(bad.Error, bad.Detail);

        var snapshot = cache.Current;
        if (snapshot is null)
        {
            if (diag.IsDebugEnabled) diag.Debug(LogCat, "Command context: cache empty; restoring local session.");
            var restored = await sessions.RestoreAsync(ct).ConfigureAwait(false);
            snapshot = restored.IsSuccess ? restored.Value : null;
            cache.Set(snapshot);
            diag.Info(LogCat, $"Session restore success={restored.IsSuccess} tenant={installation.TenantId} {DescribeSession(snapshot)}");
        }

        var user = RequireActive(snapshot);
        if (user.IsFailure)
        {
            diag.Warn(LogCat, $"Command context DENIED code={user.Error.Code} tenant={installation.TenantId} {DescribeSession(snapshot)}");
            return Result<CommandContext>.Fail(user.Error, user.Detail);
        }
        return Result<CommandContext>.Ok(NewContext(user.Value!, operationId, dependsOn, expectedVersion));
    }

    public Result<QueryContext> BuildQueryContext()
    {
        var snapshot = cache.Current;
        var user = RequireActive(snapshot);
        if (user.IsFailure)
        {
            diag.Warn(LogCat, $"Query context DENIED code={user.Error.Code} tenant={installation.TenantId} {DescribeSession(snapshot)}; no query will run.");
            return Result<QueryContext>.Fail(user.Error, user.Detail);
        }
        var context = new QueryContext(installation.TenantId, user.Value!, CurrentCorrelationId());
        if (diag.IsDebugEnabled)
            diag.Debug(LogCat, $"Query context OK tenant={context.TenantId} user={context.UserId} corr={context.CorrelationId} {DescribeSession(snapshot)}");
        return Result<QueryContext>.Ok(context);
    }

    public Result<CommandContext> BuildAnonymousCommandContext(string? operationId = null)
    {
        if (InvalidOperationId(operationId) is { } bad) return Result<CommandContext>.Fail(bad.Error, bad.Detail);
        return Result<CommandContext>.Ok(NewContext(ICurrentUserProvider.AnonymousUserId, operationId, null, null));
    }

    public QueryContext BuildAnonymousQueryContext() =>
        new(installation.TenantId, ICurrentUserProvider.AnonymousUserId, CurrentCorrelationId());

    private string DescribeSession(SessionSnapshot? snapshot) => snapshot is null
        ? "session=cache-empty"
        : $"sessionUser={snapshot.UserId ?? "signed-out"} locked={snapshot.IsLocked} expiresUtc={snapshot.ExpiresAtUtc:O} expired={snapshot.ExpiresAtUtc is not { } expires || clock.UtcNow >= expires}";

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
