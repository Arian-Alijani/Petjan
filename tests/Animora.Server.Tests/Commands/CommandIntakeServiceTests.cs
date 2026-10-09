using Animora.Contracts.Wire.Commands;
using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Persistence.PostgreSql.Commands;
using Animora.Server.Commands;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Animora.Server.Tests.Commands;

public sealed class CommandIntakeServiceTests
{
    [Fact]
    public async Task IdenticalRetry_ReturnsStoredResult_DoesNotReexecute()
    {
        var f = new IntakeTestFixture();
        var service = f.CreateService();
        var envelope = f.Envelope();
        var first = await service.SubmitAsync(envelope);
        int audits = f.Audit.Entries.Count;
        f.Clock.UtcNow += TimeSpan.FromMinutes(1);
        var replay = await service.SubmitAsync(envelope);
        Assert.True(first.IsSuccess);
        Assert.Equal(first.Value, replay.Value);
        Assert.Equal(1, f.Dispatcher.Calls);
        Assert.Equal(1, f.Outbox.Calls);
        Assert.Equal(audits, f.Audit.Entries.Count);
        Assert.Equal(f.TenantId, f.Dispatcher.LastContext!.TenantId);
        Assert.Equal(f.Dispatcher.EntityId, first.Value!.ServerEntityId);
        Assert.Single(await f.Store.GetIdMapAsync(envelope.OperationId));
    }

    [Fact]
    public async Task SameOperationId_DifferentFingerprint_Rejected_Audited()
    {
        var f = new IntakeTestFixture();
        var service = f.CreateService();
        var first = f.Envelope();
        await service.SubmitAsync(first);
        var retry = f.Envelope(payload: "{\"name\":\"different\"}", operation: first.OperationId);
        var result = await service.SubmitAsync(retry);
        Assert.Equal(ErrorCodes.IdempotencyFingerprintMismatch.Code, result.Value!.ErrorCode);
        Assert.Equal(SettlementOutcome.Rejected, result.Value.Outcome);
        Assert.Equal(1, f.Dispatcher.Calls);
        Assert.Equal("integrity", f.Audit.Entries.Last().Category);
    }

    [Fact]
    public async Task TenantMismatch_Rejected_Audited()
    {
        var f = new IntakeTestFixture();
        var result = await f.CreateService().SubmitAsync(f.Envelope() with { TenantId = IdGenerator.NewId() });
        Assert.Equal(ErrorCodes.TenantMismatch.Code, result.Value!.ErrorCode);
        Assert.Equal("command.tenant_mismatch", Assert.Single(f.Audit.Entries).Action);
        Assert.Equal(f.TenantId, f.Audit.Entries[0].TenantId);
        Assert.Equal(0, f.Store.BeginCalls);
        Assert.Equal(0, f.Dispatcher.Calls);
    }

    [Fact]
    public async Task ForgedFingerprint_Rejected_BeforeStore()
    {
        var f = new IntakeTestFixture();
        var result = await f.CreateService().SubmitAsync(f.Envelope() with { CommandFingerprint = new string('0', 64) });
        Assert.Equal(ErrorCodes.IdempotencyFingerprintMismatch.Code, result.Value!.ErrorCode);
        Assert.Equal(0, f.Store.BeginCalls);
        Assert.Single(f.Audit.Entries);
    }

    [Fact]
    public async Task DependencyUnsettled_Deferred()
    {
        var f = new IntakeTestFixture();
        var dependency = f.Envelope();
        f.Store.Rows.Add(dependency.OperationId, f.Row(dependency));
        var envelope = f.Envelope() with { DependsOnOperationId = dependency.OperationId };
        var result = await f.CreateService().SubmitAsync(envelope);
        Assert.Equal(SettlementOutcome.Deferred, result.Value!.Outcome);
        Assert.Equal(ErrorCodes.DependencyUnsettled.Code, result.Value.ErrorCode);
        Assert.Equal(IntakeStatus.InProgress, f.Store.Rows[envelope.OperationId].Status);
        Assert.Equal(0, f.Dispatcher.Calls);
    }

    [Fact]
    public async Task DependencyRejected_Rejected()
    {
        var f = new IntakeTestFixture();
        f.Authorizer.Outcome = Result.Fail(ErrorCodes.PermissionDenied);
        var service = f.CreateService();
        var dependency = f.Envelope();
        await service.SubmitAsync(dependency);
        var result = await service.SubmitAsync(f.Envelope() with { DependsOnOperationId = dependency.OperationId });
        Assert.Equal(ErrorCodes.DependencyRejected.Code, result.Value!.ErrorCode);
        Assert.Equal(SettlementOutcome.Rejected, result.Value.Outcome);
        Assert.Equal(0, f.Dispatcher.Calls);
    }

    [Fact]
    public async Task ExpectedServerVersionMismatch_Conflict()
    {
        var f = new IntakeTestFixture();
        f.Versions.Version = "11111111-1111-1111-1111-111111111111";
        string id = IdGenerator.NewId();
        var envelope = f.Envelope("test.update", $"{{\"id\":\"{id}\",\"name\":\"updated\"}}", aggregateId: id)
            with { ExpectedServerVersion = "22222222-2222-2222-2222-222222222222" };
        var result = await f.CreateService().SubmitAsync(envelope);
        Assert.Equal(SettlementOutcome.Conflict, result.Value!.Outcome);
        Assert.Equal(ErrorCodes.ConcurrencyConflict.Code, result.Value.ErrorCode);
        Assert.Equal(0, f.Dispatcher.Calls);
    }

    [Fact]
    public async Task UnknownCommandType_Rejected_HandlerNotRegistered()
    {
        var f = new IntakeTestFixture();
        var result = await f.CreateService().SubmitAsync(f.Envelope("unknown.command"));
        Assert.Equal(ErrorCodes.HandlerNotRegistered.Code, result.Value!.ErrorCode);
        Assert.Equal(0, f.Dispatcher.Calls);
    }

    [Fact]
    public async Task UnsupportedContractVersion_426()
    {
        var f = new IntakeTestFixture();
        var result = await f.CreateService().SubmitAsync(f.Envelope() with { ContractVersion = 999 });
        var context = new DefaultHttpContext();
        var http = CommandEndpoints.ToHttp(context, result);
        Assert.Equal(426, ((IStatusCodeHttpResult)http).StatusCode);
        Assert.Equal("1,2", context.Response.Headers["X-Animora-Supported-Versions"]);
        Assert.Equal(0, f.Store.BeginCalls);
    }

    [Fact]
    public async Task PermissionDenied_Rejected_BeforeHandler()
    {
        var f = new IntakeTestFixture();
        f.Authorizer.Outcome = Result.Fail(ErrorCodes.PermissionDenied);
        var result = await f.CreateService().SubmitAsync(f.Envelope());
        Assert.Equal(ErrorCodes.PermissionDenied.Code, result.Value!.ErrorCode);
        Assert.Equal(0, f.Dispatcher.Calls);
        Assert.Single(f.Audit.Entries);
    }

    [Theory]
    [InlineData(SettlementOutcome.Accepted)]
    [InlineData(SettlementOutcome.Rejected)]
    [InlineData(SettlementOutcome.Conflict)]
    [InlineData(SettlementOutcome.Deferred)]
    public void SettledOutcomes_Return_Http200(SettlementOutcome outcome)
    {
        var settlement = new SettlementResult(IdGenerator.NewId(), outcome, null, null, null, null,
            new FixedClock().UtcNow, FinancialEffect.None, null);
        var http = CommandEndpoints.ToHttp(new DefaultHttpContext(), Result<SettlementResult>.Ok(settlement));
        Assert.Equal(200, ((IStatusCodeHttpResult)http).StatusCode ?? 200);
        Assert.Equal(settlement, ((IValueHttpResult)http).Value);
    }

    [Fact]
    public async Task EntitlementDenied_Rejected_AndAcceptedVersionIsCanonical()
    {
        var f = new IntakeTestFixture();
        f.Entitlement.Outcome = Result.Fail(ErrorCodes.EntitlementRequired);
        var result = await f.CreateService().SubmitAsync(f.Envelope());
        Assert.Equal(ErrorCodes.EntitlementRequired.Code, result.Value!.ErrorCode);
        Assert.Equal(0, f.Dispatcher.Calls);
        f.Entitlement.Outcome = Result.Ok();
        f.Versions.Version = "11111111-1111-1111-1111-111111111111";
        var accepted = await f.CreateService().SubmitAsync(f.Envelope());
        Assert.Equal(f.Versions.Version, accepted.Value!.ServerVersion);
    }
}
