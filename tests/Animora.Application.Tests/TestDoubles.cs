using Animora.Application;
using Animora.Application.Abstractions;
using Animora.Application.Dispatch;
using Animora.Application.Ports;
using Animora.Application.Security;
using Animora.Application.Validation;
using Animora.Domain.Results;
using Animora.Domain.Time;
using Microsoft.Extensions.DependencyInjection;

namespace Animora.Application.Tests;

internal sealed class StepLog
{
    public List<string> Steps { get; } = [];
}

internal sealed record EchoCommand(string Text) : ICommand<string>;

internal sealed class EchoCommandHandler(StepLog log) : ICommandHandler<EchoCommand, string>
{
    public Task<Result<string>> HandleAsync(EchoCommand command, CommandContext context, CancellationToken ct)
    {
        log.Steps.Add("Handler");
        return Task.FromResult(Result<string>.Ok(command.Text));
    }
}

internal sealed record ThrowingCommand(Exception Error) : ICommand<int>;

internal sealed class ThrowingCommandHandler : ICommandHandler<ThrowingCommand, int>
{
    public Task<Result<int>> HandleAsync(ThrowingCommand command, CommandContext context, CancellationToken ct) =>
        throw command.Error;
}

internal sealed record UnregisteredCommand : ICommand<int>;

internal sealed record LoggedQuery : IQuery<int>;

internal sealed class LoggedQueryHandler(StepLog log) : IQueryHandler<LoggedQuery, int>
{
    public Task<Result<int>> HandleAsync(LoggedQuery query, QueryContext context, CancellationToken ct)
    {
        log.Steps.Add("Handler");
        return Task.FromResult(Result<int>.Ok(42));
    }
}

internal sealed class EchoValidator(StepLog log) : IValidator<EchoCommand>
{
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(EchoCommand instance, CancellationToken ct)
    {
        log.Steps.Add("Validation");
        IReadOnlyList<ValidationFailure> failures = Rules.Required(instance.Text, nameof(EchoCommand.Text)) is { } f ? [f] : [];
        return ValueTask.FromResult(failures);
    }
}

internal sealed class RecordingAuthorizationGate(StepLog log, Result verdict) : IAuthorizationGate
{
    public ValueTask<Result> EvaluateAsync(object command, CommandContext context, CancellationToken ct) => Record();

    public ValueTask<Result> EvaluateAsync(object query, QueryContext context, CancellationToken ct) => Record();

    private ValueTask<Result> Record()
    {
        log.Steps.Add("Authorization");
        return ValueTask.FromResult(verdict);
    }
}

internal sealed class RecordingEntitlementGate(StepLog log, Result verdict) : IEntitlementGate
{
    public ValueTask<Result> EvaluateAsync(object command, CommandContext context, CancellationToken ct) => Record();

    public ValueTask<Result> EvaluateAsync(object query, QueryContext context, CancellationToken ct) => Record();

    private ValueTask<Result> Record()
    {
        log.Steps.Add("Entitlement");
        return ValueTask.FromResult(verdict);
    }
}

internal sealed class RecordingUnitOfWork(StepLog log) : IUnitOfWork
{
    public int Opened { get; private set; }

    public Task<Result> SaveAsync(CancellationToken ct = default) => Task.FromResult(Result.Ok());

    public async Task<Result<T>> ExecuteAsync<T>(Func<CancellationToken, Task<Result<T>>> work, CancellationToken ct = default)
    {
        Opened++;
        log.Steps.Add("Transaction");
        return await work(ct).ConfigureAwait(false);
    }
}

internal sealed class FixedClock : IClock
{
    public DateTimeOffset UtcNow { get; } = new(2026, 9, 24, 8, 0, 0, TimeSpan.Zero);
}

/// <summary>In-memory session double: signed out until <see cref="StartAsync"/>.</summary>
internal sealed class FakeSessionService : ISessionService
{
    private SessionSnapshot _current = SessionSnapshot.SignedOut(isFirstRun: false);

    public Task<Result<SessionSnapshot>> RestoreAsync(CancellationToken ct = default) => Task.FromResult(Result<SessionSnapshot>.Ok(_current));

    public Task<Result<SessionSnapshot>> StartAsync(string userId, string deviceId, CancellationToken ct = default)
    {
        _current = new SessionSnapshot(userId, userId, Domain.Identity.StaffRole.Manager, false, false,
            new FixedClock().UtcNow.AddHours(12));
        return Task.FromResult(Result<SessionSnapshot>.Ok(_current));
    }

    public Task<Result> TouchAsync(CancellationToken ct = default) => Task.FromResult(Result.Ok());

    public Task<Result> LockAsync(CancellationToken ct = default) => Task.FromResult(Result.Ok());

    public Task<Result<SessionSnapshot>> UnlockAsync(string password, CancellationToken ct = default) =>
        Task.FromResult(Result<SessionSnapshot>.Ok(_current));

    public Task<Result> EndAsync(string reason, CancellationToken ct = default)
    {
        _current = SessionSnapshot.SignedOut(isFirstRun: false);
        return Task.FromResult(Result.Ok());
    }
}

internal sealed class FixedInstallation : ILocalInstallationIdentity
{
    public string TenantId => "T-TEST";

    public string DeviceId => "D-TEST";
}

/// <summary>Builds a provider with the full chain wired to recording doubles.</summary>
internal static class TestHost
{
    public static CommandContext CommandCtx(string? correlationId = null) => new(
        "T-TEST", "U-TEST", "D-TEST", Domain.Primitives.IdGenerator.NewId(),
        correlationId ?? Domain.Primitives.IdGenerator.NewId(), null, null, null, new FixedClock().UtcNow);

    public static QueryContext QueryCtx(string? correlationId = null) =>
        new("T-TEST", "U-TEST", correlationId ?? Domain.Primitives.IdGenerator.NewId());

    public static ServiceProvider Build(StepLog log, Result? authorization = null, Result? entitlement = null,
        bool withValidator = false, bool withUnitOfWork = true)
    {
        var services = new ServiceCollection();
        services.AddSingleton(log);
        services.AddSingleton<IAuthorizationGate>(new RecordingAuthorizationGate(log, authorization ?? Result.Ok()));
        services.AddSingleton<IEntitlementGate>(new RecordingEntitlementGate(log, entitlement ?? Result.Ok()));
        if (withUnitOfWork) services.AddSingleton<IUnitOfWork>(new RecordingUnitOfWork(log));
        services.AddSingleton<IClock, FixedClock>();
        services.AddSingleton<ILocalInstallationIdentity, FixedInstallation>();
        services.AddSingleton<ISessionService, FakeSessionService>();
        // B04a/B04b: AddAnimoraApplication registers the authorization/capability evaluators; supply the
        // repository/store/signer seams they depend on so the container (ValidateOnBuild) can construct them.
        services.AddScoped<IStaffMembershipRepository>(_ => new FakeMembershipRepository());
        services.AddScoped<IRoleDefinitionRepository>(sp => new SeededRoleRepository(sp.GetRequiredService<IClock>()));
        services.AddSingleton<ICapabilitySnapshotSigner, TestCapabilitySigner>();
        services.AddScoped<ICapabilityStore, InMemoryCapabilityStore>();
        // B02: the two sync UI-seam query handlers require the command journal; supply the in-memory double so
        // ValidateOnBuild succeeds.
        services.AddSingleton<Animora.Application.Commands.IIntegrityAlertSink, RecordingIntegritySink>();
        services.AddSingleton<Animora.Application.Commands.ICommandJournal>(sp =>
            new FakeCommandJournal(sp.GetRequiredService<Animora.Application.Commands.IIntegrityAlertSink>()));
        services.AddSingleton<Animora.Application.Commands.ILocalSequenceAllocator>(new CountingSequenceAllocator());
        services.AddAnimoraApplication(b =>
        {
            b.AddCommand<EchoCommand, string, EchoCommandHandler>()
                .AddCommand<ThrowingCommand, int, ThrowingCommandHandler>()
                .AddQuery<LoggedQuery, int, LoggedQueryHandler>();
            if (withValidator) b.AddValidator<EchoCommand, EchoValidator>();
        });
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    public static IDispatcher Dispatcher(this IServiceProvider sp) =>
        sp.CreateScope().ServiceProvider.GetRequiredService<IDispatcher>();

    public static HandlerRegistry Registry(this IServiceProvider sp) => sp.GetRequiredService<HandlerRegistry>();
}
