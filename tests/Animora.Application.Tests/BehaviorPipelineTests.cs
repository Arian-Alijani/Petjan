using Animora.Application.Abstractions;
using Animora.Application.Dispatch;
using Animora.Application.Validation;
using Animora.Domain.Results;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Animora.Application.Tests;

public sealed class BehaviorPipelineTests
{
    [Fact]
    public void Chain_IsFixedInDocumentedOrder()
    {
        Assert.Equal(
            ["Correlation", "ExceptionMapping", "Validation", "Authorization", "Entitlement", "Transaction",
                "CommandJournal"],
            Dispatcher.Chain.Select(b => b.Name).ToArray());
    }

    [Fact]
    public async Task Command_RunsStagesInExactOrder()
    {
        var log = new StepLog();
        using var sp = TestHost.Build(log, withValidator: true);
        var result = await sp.Dispatcher().SendAsync(new EchoCommand("hi"), TestHost.CommandCtx());
        Assert.Equal("hi", result.Value);
        Assert.Equal(["Validation", "Authorization", "Entitlement", "Transaction", "Handler"], log.Steps);
    }

    [Fact]
    public async Task Correlation_WrapsWholeChain()
    {
        var log = new StepLog();
        var services = new ServiceCollection();
        var corr = Domain.Primitives.IdGenerator.NewId();
        string? seen = null;
        services.AddSingleton(log);
        services.AddSingleton<IUnitOfWork>(new RecordingUnitOfWork(log));
        services.AddSingleton<Security.IAuthorizationGate>(sp => new ProbeGate(sp, id => seen = id));
        services.AddAnimoraApplication(b => b.AddCommand<EchoCommand, string, EchoCommandHandler>());
        using var provider = services.BuildServiceProvider();
        await provider.Dispatcher().SendAsync(new EchoCommand("x"), TestHost.CommandCtx(corr));
        Assert.Equal(corr, seen); // correlation began before authorization ran
    }

    [Fact]
    public async Task FailingValidator_ShortCircuitsWithValidationFailed()
    {
        var log = new StepLog();
        using var sp = TestHost.Build(log, withValidator: true);
        var result = await sp.Dispatcher().SendAsync(new EchoCommand(" "), TestHost.CommandCtx());
        Assert.Equal(ErrorCodes.ValidationFailed, result.Error);
        Assert.Equal([new ValidationFailure(nameof(EchoCommand.Text), Rules.RequiredCode)], ValidationFailure.ParseDetail(result.Detail));
        Assert.Equal(["Validation"], log.Steps); // handler never invoked
    }

    [Fact]
    public async Task DenyingAuthorization_ReturnsPermissionDenied_BeforeEntitlement()
    {
        var log = new StepLog();
        using var sp = TestHost.Build(log, authorization: Result.Fail(ErrorCodes.Unexpected, "no"));
        var result = await sp.Dispatcher().SendAsync(new EchoCommand("x"), TestHost.CommandCtx());
        Assert.Equal(ErrorCodes.PermissionDenied, result.Error);
        Assert.Equal(["Authorization"], log.Steps);
    }

    [Fact]
    public async Task RefusingEntitlement_ReturnsEntitlementRequired()
    {
        var log = new StepLog();
        using var sp = TestHost.Build(log, entitlement: Result.Fail(ErrorCodes.EntitlementRequired));
        var result = await sp.Dispatcher().SendAsync(new EchoCommand("x"), TestHost.CommandCtx());
        Assert.Equal(ErrorCodes.EntitlementRequired, result.Error);
        Assert.Equal(["Authorization", "Entitlement"], log.Steps);
    }

    [Fact]
    public async Task Query_NeverOpensTransaction()
    {
        var log = new StepLog();
        using var sp = TestHost.Build(log);
        var result = await sp.Dispatcher().QueryAsync(new LoggedQuery(), TestHost.QueryCtx());
        Assert.Equal(42, result.Value);
        Assert.Equal(["Authorization", "Entitlement", "Handler"], log.Steps);
        Assert.Equal(0, ((RecordingUnitOfWork)sp.GetRequiredService<IUnitOfWork>()).Opened);
    }

    private sealed class ProbeGate(IServiceProvider sp, Action<string?> observe) : Security.IAuthorizationGate
    {
        public ValueTask<Result> EvaluateAsync(object command, Abstractions.CommandContext context, CancellationToken ct)
        {
            observe(sp.GetRequiredService<Correlation.ICorrelationContext>().CurrentId);
            return ValueTask.FromResult(Result.Ok());
        }

        public ValueTask<Result> EvaluateAsync(object query, Abstractions.QueryContext context, CancellationToken ct) =>
            ValueTask.FromResult(Result.Ok());
    }
}
