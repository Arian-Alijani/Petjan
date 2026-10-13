using Animora.Application.Abstractions;
using Animora.Application.Correlation;
using Animora.Application.Ports;
using Animora.Application.Security;
using Animora.Application.UseCases.Diagnostics;
using Animora.Domain.Results;
using Animora.Domain.Time;
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
        // B04a: the app defaults now include the real authorization gate, so a non-pre-auth request needs the
        // security seams (installation identity + membership/role repositories) and an active session.
        var services = new ServiceCollection()
            .AddSingleton<IClock, FixedClock>()
            .AddSingleton<ILocalInstallationIdentity, FixedInstallation>()
            .AddSingleton<ISessionService, FakeSessionService>()
            .AddScoped<IStaffMembershipRepository>(_ => new FakeMembershipRepository())
            .AddScoped<IRoleDefinitionRepository>(sp => new SeededRoleRepository(sp.GetRequiredService<IClock>()));
        services.AddAnimoraApplication();
        using var sp = services.BuildServiceProvider();

        var session = await sp.GetRequiredService<ISessionService>().StartAsync("U-TEST", "D-TEST");
        sp.GetRequiredService<SessionContextCache>().Set(session.Value);

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
    public async Task SessionCurrentUserProvider_RequiresSession_AndBuildsContexts()
    {
        using var sp = TestHost.Build(new StepLog());
        using var scope = sp.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<ICurrentUserProvider>();
        Assert.Equal(ErrorCodes.SessionExpired, (await provider.BuildCommandContextAsync(null, null, 3, CancellationToken.None)).Error);
        Assert.Equal(ErrorCodes.SessionExpired, provider.BuildQueryContext().Error);

        var session = await sp.GetRequiredService<ISessionService>().StartAsync("U-TEST", "D-TEST");
        sp.GetRequiredService<SessionContextCache>().Set(session.Value);
        var ctx = await provider.BuildCommandContextAsync(null, null, 3, CancellationToken.None);
        Assert.True(ctx.IsSuccess);
        Assert.Equal("T-TEST", ctx.Value!.TenantId);
        Assert.Equal("U-TEST", ctx.Value.UserId);
        Assert.Equal("D-TEST", ctx.Value.DeviceId);
        Assert.Null(Validation.Rules.UlidFormat(ctx.Value.OperationId, "op"));
        Assert.Equal(3, ctx.Value.ExpectedAggregateVersion);
        Assert.Equal(ErrorCodes.ValidationFailed,
            (await provider.BuildCommandContextAsync("not-a-ulid", null, null, CancellationToken.None)).Error);
        Assert.Equal("U-TEST", provider.BuildQueryContext().Value!.UserId);
        Assert.Equal(ICurrentUserProvider.AnonymousUserId, provider.BuildAnonymousCommandContext().Value!.UserId);
    }

    [Theory]
    [InlineData("missing", "SESSION_EXPIRED")]
    [InlineData("signed-out", "SESSION_EXPIRED")]
    [InlineData("expired", "SESSION_EXPIRED")]
    [InlineData("locked", "PERMISSION_DENIED")]
    [InlineData("active", null)]
    public void SessionDiagnostics_ExplainReadDenialWithoutChangingContext(string state, string? code)
    {
        var diagnostics = new RecordingDiagnostics();
        var clock = new FixedClock();
        var cache = new SessionContextCache();
        cache.Set(state switch
        {
            "missing" => null,
            "signed-out" => SessionSnapshot.SignedOut(false),
            _ => new SessionSnapshot("U-TEST", "private-display-name", Domain.Identity.StaffRole.Manager,
                state == "locked", false, state == "expired" ? clock.UtcNow : clock.UtcNow.AddHours(1)),
        });
        var correlation = new CorrelationContext();
        using var correlationScope = correlation.Begin("COR-TEST");
        var provider = new SessionCurrentUserProvider(new FixedInstallation(), new FakeSessionService(), cache,
            correlation, new OperationIdFactory(), clock, diagnostics);
        var result = provider.BuildQueryContext();
        Assert.Equal(code is null, result.IsSuccess);
        var text = string.Join("\n", diagnostics.Messages);
        if (code is not null) Assert.Contains($"DENIED code={code} tenant=T-TEST", text);
        else
        {
            Assert.Equal("T-TEST", result.Value!.TenantId);
            Assert.Equal("U-TEST", result.Value.UserId);
            Assert.Equal("COR-TEST", result.Value.CorrelationId);
            Assert.Contains("Query context OK", text);
        }
        Assert.DoesNotContain("private-display-name", text);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DiagnosticsRegistration_NullDefaultDoesNotOverrideHost(bool hostFirst)
    {
        var diagnostics = new RecordingDiagnostics();
        var services = new ServiceCollection();
        if (hostFirst) services.AddSingleton<Animora.Contracts.Diagnostics.IClientDiagnosticsLog>(diagnostics);
        services.AddAnimoraApplication();
        if (!hostFirst) services.AddSingleton<Animora.Contracts.Diagnostics.IClientDiagnosticsLog>(diagnostics);
        using var provider = services.BuildServiceProvider();
        Assert.Same(diagnostics, provider.GetRequiredService<Animora.Contracts.Diagnostics.IClientDiagnosticsLog>());
    }

    private sealed class RecordingDiagnostics : Animora.Contracts.Diagnostics.IClientDiagnosticsLog
    {
        public bool IsDebugEnabled => true;
        public List<string> Messages { get; } = [];
        public void Info(string category, string message) => Messages.Add(message);
        public void Warn(string category, string message) => Messages.Add(message);
        public void Debug(string category, string message) => Messages.Add(message);
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
