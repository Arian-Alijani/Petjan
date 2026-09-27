using Animora.Application.Auditing;
using Animora.Application.Behaviors;
using Animora.Application.Dispatch;
using Animora.Domain.Results;
using Animora.Domain.Time;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Animora.Application.Tests;

public sealed class AuditBehaviorTests
{
    [Theory]
    [InlineData(true, "", AuditOutcome.Success)]
    [InlineData(false, "PERMISSION_DENIED", AuditOutcome.Denied)]
    [InlineData(false, "STORE_UNAVAILABLE", AuditOutcome.Failed)]
    public async Task RecordsExpectedOutcome(bool succeeds, string code, AuditOutcome expected)
    {
        var writer = new RecordingAuditWriter(Result.Ok());
        using var services = new ServiceCollection().AddSingleton<IAuditWriter>(writer)
            .AddSingleton<IClock, FixedClock>().BuildServiceProvider();
        var behavior = new AuditBehavior();
        var descriptor = new PipelineDescriptor(typeof(object), typeof(int), RequestKind.Command, false,
            Audit: new AuditBinding(AuditCategory.Maintenance, "test"));
        PipelineStep<int> next = (_, _, _, _) => Task.FromResult(succeeds
            ? Result<int>.Ok(1)
            : Result<int>.Fail(code == "PERMISSION_DENIED" ? ErrorCodes.PermissionDenied : ErrorCodes.StoreUnavailable));

        var result = await behavior.Wrap<object, int>(next, descriptor)(services, new object(), TestHost.CommandCtx(), default);
        Assert.Equal(succeeds, result.IsSuccess);
        Assert.Equal(expected, Assert.Single(writer.Records).Outcome);
    }

    [Fact]
    public async Task AuditFailureFailsClosed()
    {
        var writer = new RecordingAuditWriter(Result.Fail(ErrorCodes.StoreUnavailable));
        using var services = new ServiceCollection().AddSingleton<IAuditWriter>(writer)
            .AddSingleton<IClock, FixedClock>().BuildServiceProvider();
        var descriptor = new PipelineDescriptor(typeof(object), typeof(int), RequestKind.Command, false,
            Audit: new AuditBinding(AuditCategory.Maintenance, "test"));
        PipelineStep<int> next = (_, _, _, _) => Task.FromResult(Result<int>.Ok(1));
        var result = await new AuditBehavior().Wrap<object, int>(next, descriptor)
            (services, new object(), TestHost.CommandCtx(), default);
        Assert.Equal(ErrorCodes.AuditWriteFailed, result.Error);
    }

    private sealed class RecordingAuditWriter(Result result) : IAuditWriter
    {
        public List<AuditRecord> Records { get; } = [];
        public void Enlist(AuditRecord record) => Records.Add(record);
        public Task<Result> WriteAsync(AuditRecord record, CancellationToken ct = default)
        {
            Records.Add(record);
            return Task.FromResult(result);
        }
    }
}
