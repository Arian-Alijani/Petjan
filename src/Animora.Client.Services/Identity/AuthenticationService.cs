using Animora.Application.Abstractions;
using Animora.Application.Security;
using Animora.Application.UseCases.Identity;
using Animora.Contracts.Identity;
using Animora.Domain.Identity;
using Animora.Domain.Results;
using Microsoft.Extensions.DependencyInjection;

namespace Animora.Client.Services.Identity;

/// <summary>
/// <see cref="IAuthenticationService"/> over the B03b identity use cases. Singleton: the scoped
/// <see cref="IDispatcher"/> and <see cref="ICurrentUserProvider"/> are resolved from a fresh async scope per
/// operation, never captured. Awaits intentionally do not use <c>ConfigureAwait(false)</c> (results return to a
/// ViewModel — CONVENTIONS §4).
/// </summary>
public sealed class AuthenticationService(IServiceScopeFactory scopes, CapabilityGateService gate, IOnlineAuthClient? online = null)
    : IAuthenticationService
{
    public async Task<AuthOutcome> SignInAsync(string username, string password, bool rememberDevice,
        CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var (dispatcher, provider) = Resolve(scope);

        var context = provider.BuildAnonymousCommandContext();
        if (context.IsFailure)
        {
            gate.Clear(); // fail closed: no session ⇒ no authority.
            return new AuthOutcome(false, context.Error.Code, null);
        }

        var result = await dispatcher.SendAsync(new SignInCommand(username, password, rememberDevice), context.Value!, ct);
        if (!result.IsSuccess)
        {
            gate.Clear(); // keep the gate read-only/denied when credentials are refused.
            return new AuthOutcome(false, result.Error.Code, null);
        }

        // The shell is constructed the instant this returns, so warm the capability gate first (B04c).
        await gate.RefreshAsync(ct);
        return new AuthOutcome(true, null, result.Value!.DisplayName);
    }

    public async Task SignOutAsync(string reason, CancellationToken ct = default)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            // Sign-out must work even if the capability gate or command context has already expired.
            var ended = await scope.ServiceProvider.GetRequiredService<ISessionService>().EndAsync(reason, ct);
            if (ended.IsFailure) throw new InvalidOperationException("Local sign-out failed: " + ended.Error.Code);
        }
        finally
        {
            gate.Clear();
            await using var cleanup = scopes.CreateAsyncScope();
            cleanup.ServiceProvider.GetRequiredService<SessionContextCache>().Set(SessionSnapshot.SignedOut(isFirstRun: false));
            // Offline verifiers are intentionally retained for the currently bound clinic, but bearer authority is not.
            if (online is not null) await online.LogoutAsync(CancellationToken.None);
        }
    }

    public async Task<bool> IsFirstRunAsync(CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var (dispatcher, provider) = Resolve(scope);

        var result = await dispatcher.QueryAsync(new CurrentSessionQuery(), provider.BuildAnonymousQueryContext(), ct);
        return result.IsSuccess && result.Value!.IsFirstRun; // a failure is never treated as first run.
    }

    public async Task<AuthOutcome> CompleteFirstRunAsync(FirstRunRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await using var scope = scopes.CreateAsyncScope();
        var (dispatcher, provider) = Resolve(scope);

        var context = provider.BuildAnonymousCommandContext();
        if (context.IsFailure)
        {
            gate.Clear();
            return new AuthOutcome(false, context.Error.Code, null);
        }

        var command = new CompleteFirstRunSetupCommand(
            request.ClinicName,
            request.OwnerFullName,
            request.Username,
            request.Password);
        var result = await dispatcher.SendAsync(command, context.Value!, ct);
        if (!result.IsSuccess)
        {
            gate.Clear();
            return new AuthOutcome(false, result.Error.Code, null);
        }

        // First-run opens the Manager shell without a later sign-in, so warm the gate here too (B04c).
        await gate.RefreshAsync(ct);
        return new AuthOutcome(true, null, result.Value!.DisplayName);
    }

    public async Task<AuthOutcome> EnsureLocalMirrorAsync(ServerIdentityMirrorRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Enum.TryParse<StaffRole>(request.ServerRole?.Trim(), ignoreCase: true, out var role) || !Enum.IsDefined(role))
            return new AuthOutcome(false, ErrorCodes.ValidationFailed.Code, null);

        await using var scope = scopes.CreateAsyncScope();

        // Fail closed: only an identity the server has actually authenticated (a stored, matching online session) may be mirrored.
        var stored = await scope.ServiceProvider.GetRequiredService<ISessionStore>().ReadAsync(ct);
        if (stored.IsFailure || stored.Value is not { } session
            || !string.Equals(session.UserId, request.ServerUserId, StringComparison.Ordinal)
            || !string.Equals(session.TenantId, request.ServerTenantId, StringComparison.Ordinal))
            return new AuthOutcome(false, ErrorCodes.SessionExpired.Code, null);

        var (dispatcher, provider) = Resolve(scope);
        var context = provider.BuildAnonymousCommandContext();
        if (context.IsFailure) return new AuthOutcome(false, context.Error.Code, null);

        var command = new ProvisionLocalMirrorUserCommand(request.Username, request.Username, request.Password, role);
        var result = await dispatcher.SendAsync(command, context.Value!, ct);
        return result.IsSuccess
            ? new AuthOutcome(true, null, request.Username)
            : new AuthOutcome(false, result.Error.Code, null);
    }

    private static (IDispatcher Dispatcher, ICurrentUserProvider Provider) Resolve(AsyncServiceScope scope) =>
        (scope.ServiceProvider.GetRequiredService<IDispatcher>(),
            scope.ServiceProvider.GetRequiredService<ICurrentUserProvider>());
}
