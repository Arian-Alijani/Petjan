using Animora.Application.Abstractions;
using Animora.Application.Correlation;
using Animora.Application.Security;
using Animora.Application.UseCases.Diagnostics;
using Animora.Domain.Results;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Animora.Application.Tests;

public sealed class DispatcherTests
{
    [Fact]
    public async Task PingQuery_RoundTripsThroughChain()
    {
        using var sp = TestHost.Build(new StepLog());
        var result = await sp.Dispatcher().QueryAsync(new PingQuery(), TestHost.QueryCtx());
        Assert.True(result.IsSuccess);
        Assert.Equal(PingQueryHandler.Pong, result.Value);
    }

    [Fact]
    public async Task PingQuery_WorksWithDefaultsOnly()
    {
        var services = new ServiceCollection().AddAnimoraApplication();
        using var sp = services.BuildServiceProvider();
        var result = await sp.Dispatcher().QueryAsync(new PingQuery(), TestHost.QueryCtx());
        Assert.Equal(PingQueryHandler.Pong, result.Value);
    }

    [Fact]
    public async Task UnregisteredCommand_ReturnsHandlerNotRegistered()
    {
        using var sp = TestHost.Build(new StepLog());
        var result = await sp.Dispatcher().SendAsync(new UnregisteredCommand(), TestHost.CommandCtx());
        Assert.Equal(ErrorCodes.HandlerNotRegistered, result.Error);
    }

    [Fact]
    public void RequiredCommandWithoutHandler_FailsAtStartup()
    {
        var services = new ServiceCollection();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddAnimoraApplication(b => b.RequireCommand<UnregisteredCommand, int>()));
        Assert.Contains("HANDLER_NOT_REGISTERED", ex.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(UnregisteredCommand), ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateHandler_FailsAtStartup()
    {
        var services = new ServiceCollection();
        Assert.Throws<InvalidOperationException>(() => services.AddAnimoraApplication(b => b
            .AddCommand<EchoCommand, string, EchoCommandHandler>()
            .AddCommand<EchoCommand, string, EchoCommandHandler>()));
    }

    [Fact]
    public void ValidatorWithoutHandler_FailsAtStartup()
    {
        var services = new ServiceCollection();
        Assert.Throws<InvalidOperationException>(() =>
            services.AddAnimoraApplication(b => b.AddValidator<EchoCommand, EchoValidator>()));
    }

    [Fact]
    public async Task SqliteLikeException_SurfacesAsStoreUnavailable()
    {
        using var sp = TestHost.Build(new StepLog());
        var result = await sp.Dispatcher().SendAsync(new ThrowingCommand(new Microsoft.Data.Sqlite.SqliteException("locked", 5)),
            TestHost.CommandCtx());
        Assert.Equal(ErrorCodes.StoreUnavailable, result.Error);
        Assert.DoesNotContain(" at ", result.Detail, StringComparison.Ordinal); // never a stack trace
    }

    [Fact]
    public async Task ExceptionTable_MapsIoConcurrencyAndUnexpected()
    {
        using var sp = TestHost.Build(new StepLog());
        var d = sp.Dispatcher();
        Assert.Equal(ErrorCodes.StoreUnavailable, (await d.SendAsync(new ThrowingCommand(new IOException()), TestHost.CommandCtx())).Error);
        Assert.Equal(ErrorCodes.ConcurrencyConflict,
            (await d.SendAsync(new ThrowingCommand(new Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException()), TestHost.CommandCtx())).Error);
        Assert.Equal(ErrorCodes.Unexpected,
            (await d.SendAsync(new ThrowingCommand(new InvalidOperationException("boom")), TestHost.CommandCtx())).Error);
    }

    [Fact]
    public async Task OperationCanceled_IsRethrown()
    {
        using var sp = TestHost.Build(new StepLog());
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            sp.Dispatcher().SendAsync(new ThrowingCommand(new OperationCanceledException()), TestHost.CommandCtx()));
    }

    [Fact]
    public async Task Failure_DetailCarriesCorrelationPrefix()
    {
        using var sp = TestHost.Build(new StepLog());
        var corr = Domain.Primitives.IdGenerator.NewId();
        var result = await sp.Dispatcher().SendAsync(new UnregisteredCommand(), TestHost.CommandCtx(corr));
        Assert.Equal(ErrorCodes.HandlerNotRegistered, result.Error); // resolved before the chain: no prefix needed
        var mapped = await sp.Dispatcher().SendAsync(new ThrowingCommand(new IOException()), TestHost.CommandCtx(corr));
        Assert.StartsWith($"[corr:{corr}]", mapped.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CommandWithoutUnitOfWork_FailsClosed()
    {
        using var sp = TestHost.Build(new StepLog(), withUnitOfWork: false);
        var result = await sp.Dispatcher().SendAsync(new EchoCommand("x"), TestHost.CommandCtx());
        Assert.Equal(ErrorCodes.DispatchFailed, result.Error);
    }

    [Fact]
    public async Task SingleUserProvider_BuildsContextsWithOperationId()
    {
        using var sp = TestHost.Build(new StepLog());
        using var scope = sp.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<ICurrentUserProvider>();
        var ctx = await provider.BuildCommandContextAsync(null, null, 3, CancellationToken.None);
        Assert.True(ctx.IsSuccess);
        Assert.Equal("T-TEST", ctx.Value!.TenantId);
        Assert.Equal(SingleUserProvider.LocalOwnerUserId, ctx.Value.UserId);
        Assert.Equal("D-TEST", ctx.Value.DeviceId);
        Assert.Null(Validation.Rules.UlidFormat(ctx.Value.OperationId, "op"));
        Assert.Equal(3, ctx.Value.ExpectedAggregateVersion);
        Assert.Equal(ErrorCodes.ValidationFailed,
            (await provider.BuildCommandContextAsync("not-a-ulid", null, null, CancellationToken.None)).Error);
        Assert.Equal("T-TEST", provider.BuildQueryContext().Value!.TenantId);
    }

    [Fact]
    public void CorrelationContext_BeginsAndRestores()
    {
        var correlation = new CorrelationContext();
        Assert.Null(correlation.CurrentId);
        using (correlation.Begin("OUTER"))
        {
            using (correlation.Begin()) Assert.Equal("OUTER", correlation.CurrentId); // propagates
            using (correlation.Begin("INNER")) Assert.Equal("INNER", correlation.CurrentId);
            Assert.Equal("OUTER", correlation.CurrentId);
        }
        Assert.Null(correlation.CurrentId);
        using (correlation.Begin()) Assert.NotNull(correlation.CurrentId); // mints a ULID
    }
}
