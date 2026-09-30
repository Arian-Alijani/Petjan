using Animora.Application.Abstractions;
using Animora.Application.Ports;
using Animora.Application.Security;
using Animora.Application.UseCases.Identity;
using Animora.Domain.Identity;
using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Domain.Time;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Animora.Application.Tests;

/// <summary>
/// B04c acceptance for the B04a dispatcher gate (<see cref="PermissionAuthorizationGate"/>) exercised through the
/// real pipeline: a denied attributed command short-circuits with <c>PERMISSION_DENIED</c> and never reaches its
/// handler; an unannotated command still requires a live session; and only the real pre-authentication allowlist
/// may run on the anonymous identity.
/// </summary>
public sealed class AuthorizationGateTests
{
    private const string User = "U-TEST";
    private static readonly string Tenant = new FixedInstallation().TenantId;
    private static readonly string Device = new FixedInstallation().DeviceId;
    private static readonly DateTimeOffset Now = new FixedClock().UtcNow;

    [Fact]
    public async Task DeniedAttributedCommand_ReturnsPermissionDenied_AndHandlerIsNeverCalled()
    {
        var (provider, counter, cache, memberships) = BuildHost();
        using var _ = provider;
        // Receptionist: the role grants patients.read but not reports.read, so the RBAC stage denies.
        memberships.Add(StaffMembership.Create(Tenant, User, StaffRole.Receptionist, new FixedClock()));
        cache.Set(Session(User, StaffRole.Receptionist));

        var result = await SendAsync(provider, new GatedDeniedCommand("x"), TestHost.CommandCtx());

        Assert.Equal(ErrorCodes.PermissionDenied, result.Error);
        Assert.Equal(0, counter.Calls);
    }

    [Fact]
    public async Task AllowedAttributedCommand_RunsItsHandler()
    {
        var (provider, counter, cache, memberships) = BuildHost();
        using var _ = provider;
        memberships.Add(StaffMembership.Create(Tenant, User, StaffRole.Receptionist, new FixedClock()));
        cache.Set(Session(User, StaffRole.Receptionist));

        var result = await SendAsync(provider, new GatedAllowedCommand("hi"), TestHost.CommandCtx());

        Assert.True(result.IsSuccess);
        Assert.Equal("hi", result.Value);
        Assert.Equal(1, counter.Calls);
    }

    [Fact]
    public async Task UnannotatedCommand_WithoutActiveSession_IsDenied()
    {
        var (provider, counter, _, _) = BuildHost();
        using var _ = provider; // no session cached ⇒ signed out.

        var result = await SendAsync(provider, new PlainCommand("x"), TestHost.CommandCtx());

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCodes.SessionExpired, result.Error);
        Assert.Equal(0, counter.Calls);
    }

    [Fact]
    public async Task OnlyThePreAuthenticationAllowlist_MayUseTheAnonymousIdentity()
    {
        var (provider, _, _, _) = BuildHost();
        using var _ = provider;
        using var scope = provider.CreateScope();
        var gate = scope.ServiceProvider.GetRequiredService<IAuthorizationGate>();

        var anonymousCommand = new CommandContext(Tenant, ICurrentUserProvider.AnonymousUserId, Device,
            IdGenerator.NewId(), IdGenerator.NewId(), null, null, null, Now);
        var anonymousQuery = new QueryContext(Tenant, ICurrentUserProvider.AnonymousUserId, IdGenerator.NewId());

        // The four real pre-auth requests pass with the anonymous identity.
        Assert.True((await gate.EvaluateAsync(new SignInCommand("owner", "Secret123", false), anonymousCommand,
            CancellationToken.None)).IsSuccess);
        Assert.True((await gate.EvaluateAsync(new CurrentSessionQuery(), anonymousQuery, CancellationToken.None)).IsSuccess);

        // Any other request on the anonymous identity requires a session and is refused.
        var other = await gate.EvaluateAsync(new PlainCommand("x"), anonymousCommand, CancellationToken.None);
        Assert.False(other.IsSuccess);
        Assert.Equal(ErrorCodes.SessionExpired, other.Error);
    }

    [Fact]
    public async Task SuspendedMembership_WithOldSession_CannotUseUnannotatedCommands()
    {
        var (provider, counter, cache, memberships) = BuildHost();
        using var _ = provider;
        var member = StaffMembership.Create(Tenant, User, StaffRole.Receptionist, new FixedClock());
        memberships.Add(member);
        cache.Set(Session(User, StaffRole.Receptionist));
        Assert.True((await SendAsync(provider, new PlainCommand("before"), TestHost.CommandCtx())).IsSuccess);
        member.Suspend();
        var denied = await SendAsync(provider, new PlainCommand("after"), TestHost.CommandCtx());
        Assert.Equal(ErrorCodes.PermissionDenied, denied.Error);
        Assert.Equal(1, counter.Calls);
    }

    private static SessionSnapshot Session(string userId, StaffRole role) =>
        new(userId, userId, role, IsLocked: false, IsFirstRun: false, Now.AddHours(12));

    private static async Task<Result<string>> SendAsync(ServiceProvider provider, ICommand<string> command,
        CommandContext context)
    {
        using var scope = provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IDispatcher>().SendAsync(command, context);
    }

    private static (ServiceProvider Provider, CallCounter Counter, SessionContextCache Cache,
        FakeMembershipRepository Memberships) BuildHost()
    {
        var clock = new FixedClock();
        var counter = new CallCounter();
        var cache = new SessionContextCache();
        var memberships = new FakeMembershipRepository();
        var roles = new SeededRoleRepository(clock);

        var services = new ServiceCollection();
        services.AddSingleton<IClock>(clock);
        services.AddSingleton<ILocalInstallationIdentity, FixedInstallation>();
        services.AddSingleton<IUnitOfWork>(new RecordingUnitOfWork(new StepLog()));
        services.AddSingleton(cache);
        services.AddSingleton<IStaffMembershipRepository>(memberships);
        services.AddSingleton<ISessionService, FakeSessionService>();
        services.AddSingleton(counter);
        // The real B04a evaluator over in-memory stores (registered first: TryAddScoped in AddAnimoraApplication wins nothing).
        services.AddScoped<IPermissionEvaluator>(_ => new PermissionEvaluator(memberships, roles,
            new InvoiceAuthorizationRule(), new CashSessionAuthorizationRule()));
        services.AddAnimoraApplication(b => b
            .AddIdentityUseCases()
            .AddCommand<GatedDeniedCommand, string, GatedDeniedHandler>()
            .AddCommand<GatedAllowedCommand, string, GatedAllowedHandler>()
            .AddCommand<PlainCommand, string, PlainHandler>());
        return (services.BuildServiceProvider(), counter, cache, memberships);
    }

    private sealed class CallCounter
    {
        public int Calls { get; private set; }

        public void Hit() => Calls++;
    }

    [RequiresPermission(PermissionCodes.ReportsRead)]
    private sealed record GatedDeniedCommand(string Text) : ICommand<string>;

    private sealed class GatedDeniedHandler(CallCounter counter) : ICommandHandler<GatedDeniedCommand, string>
    {
        public Task<Result<string>> HandleAsync(GatedDeniedCommand command, CommandContext context, CancellationToken ct)
        {
            counter.Hit();
            return Task.FromResult(Result<string>.Ok(command.Text));
        }
    }

    [RequiresPermission(PermissionCodes.PatientsRead)]
    private sealed record GatedAllowedCommand(string Text) : ICommand<string>;

    private sealed class GatedAllowedHandler(CallCounter counter) : ICommandHandler<GatedAllowedCommand, string>
    {
        public Task<Result<string>> HandleAsync(GatedAllowedCommand command, CommandContext context, CancellationToken ct)
        {
            counter.Hit();
            return Task.FromResult(Result<string>.Ok(command.Text));
        }
    }

    private sealed record PlainCommand(string Text) : ICommand<string>;

    private sealed class PlainHandler(CallCounter counter) : ICommandHandler<PlainCommand, string>
    {
        public Task<Result<string>> HandleAsync(PlainCommand command, CommandContext context, CancellationToken ct)
        {
            counter.Hit();
            return Task.FromResult(Result<string>.Ok(command.Text));
        }
    }
}
