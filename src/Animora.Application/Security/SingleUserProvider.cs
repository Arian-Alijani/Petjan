using Animora.Application.Abstractions;
using Animora.Application.Correlation;
using Animora.Application.Ports;
using Animora.Application.Validation;
using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Domain.Time;

namespace Animora.Application.Security;

/// <summary>
/// Local single-user stub: tenant from the local installation, fixed user <see cref="LocalOwnerUserId"/>,
/// device from the installation identity. <b>Replaced by B03.</b>
/// </summary>
public sealed class SingleUserProvider(
    ILocalInstallationIdentity installation,
    ICorrelationContext correlation,
    OperationIdFactory operationIds,
    IClock clock) : ICurrentUserProvider
{
    public const string LocalOwnerUserId = "U-LOCAL-OWNER";

    public Task<Result<CommandContext>> BuildCommandContextAsync(string? operationId, string? dependsOn,
        long? expectedVersion, CancellationToken ct)
    {
        if (operationId is not null && Rules.UlidFormat(operationId, nameof(operationId)) is { } bad)
            return Task.FromResult(Result<CommandContext>.Fail(ErrorCodes.ValidationFailed,
                ValidationFailure.FormatDetail([bad])));

        var context = new CommandContext(
            installation.TenantId,
            LocalOwnerUserId,
            installation.DeviceId,
            operationId ?? operationIds.New(),
            CurrentCorrelationId(),
            CausationId: null,
            DependsOnOperationId: dependsOn,
            ExpectedAggregateVersion: expectedVersion,
            CreatedAtUtc: clock.UtcNow);
        return Task.FromResult(Result<CommandContext>.Ok(context));
    }

    public Result<QueryContext> BuildQueryContext() =>
        Result<QueryContext>.Ok(new QueryContext(installation.TenantId, LocalOwnerUserId, CurrentCorrelationId()));

    private string CurrentCorrelationId() => correlation.CurrentId ?? IdGenerator.NewId();
}
