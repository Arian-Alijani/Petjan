using Animora.Application.Abstractions;
using Animora.Application.Security;
using Animora.Application.UseCases.Identity;
using Animora.Contracts.Diagnostics;
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
public sealed class AuthenticationService(IServiceScopeFactory scopes, CapabilityGateService gate, IOnlineAuthClient? online = null,
    LiveAccessRefreshService? liveAccess = null)
    : IAuthenticationService
{
    public async Task<AuthOutcome> SignInAsync(string username, string password, bool rememberDevice,
        CancellationToken ct = default)
    {
        TraceLog.Info($"[auth] SignInAsync BEGIN user='{username}' remember={rememberDevice}", "auth");
        gate.Clear(); // fence any in-flight refresh belonging to the previous login
        await using var scope = scopes.CreateAsyncScope();
        var (dispatcher, provider) = Resolve(scope);

        var context = provider.BuildAnonymousCommandContext();
        if (context.IsFailure)
        {
            gate.Clear(); // fail closed: no session ⇒ no authority.
            TraceLog.Warn($"[auth] SignInAsync → no anonymous command context ({context.Error}); gate CLEARED (read-only).", "auth");
            return new AuthOutcome(false, context.Error.Code, null);
        }

        var result = await dispatcher.SendAsync(new SignInCommand(username, password, rememberDevice, PreserveServerCapabilities: online is not null), context.Value!, ct);
        if (!result.IsSuccess)
        {
            gate.Clear(); // keep the gate read-only/denied when credentials are refused.
            TraceLog.Warn($"[auth] SignInAsync → dispatcher REFUSED ({result.Error}); gate CLEARED (read-only).", "auth");
            return new AuthOutcome(false, result.Error.Code, null);
        }

        // The shell is constructed the instant this returns, so warm the capability gate first (B04c).
        TraceLog.Info("[auth] SignInAsync → success; warming capability gate + live access before the shell is built.", "auth");
        await gate.RefreshAsync(ct);
        if (liveAccess is not null) await liveAccess.RefreshAsync(ct);
        TraceLog.Info($"[auth] SignInAsync ← gate after warm-up: {gate.DescribeState()}", "auth");
        return new AuthOutcome(true, null, result.Value!.DisplayName);
    }

    public async Task SignOutAsync(string reason, CancellationToken ct = default)
    {
        TraceLog.Warn($"[auth] SignOutAsync BEGIN reason='{reason}' — gate will be CLEARED (all authority dropped).", "auth");
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
            if (online is not null)
            {
                var ended = await online.LogoutAsync(CancellationToken.None);
                if (ended.IsFailure) throw new InvalidOperationException("Online session cleanup failed: " + ended.Error.Code);
            }
            TraceLog.Warn("[auth] SignOutAsync DONE — gate is now deny-all (expected until the next sign-in).", "auth");
        }
    }

    public async Task<AuthOutcome> TryResumeSessionAsync(CancellationToken ct = default)
    {
        TraceLog.Info("[auth] TryResumeSessionAsync BEGIN.", "auth");
        gate.Clear(); // fence any stale authority before we decide whether a session can be resumed
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var (dispatcher, provider) = Resolve(scope);

            // CurrentSessionQuery → ISessionService.RestoreAsync: an expired session is ended, an idle one resumes
            // locked, and the shared SessionContextCache is updated as a side effect (see CurrentSessionQueryHandler).
            var result = await dispatcher.QueryAsync(new CurrentSessionQuery(), provider.BuildAnonymousQueryContext(), ct);
            if (!result.IsSuccess) return new AuthOutcome(false, result.Error.Code, null);

            var snapshot = result.Value!;
            // Only a live, unlocked, non-first-run session may silently open the shell. A locked (idle-timed-out)
            // session still requires the user to re-authenticate, so it is NOT an auto-login.
            if (snapshot.UserId is null || snapshot.IsLocked || snapshot.IsFirstRun)
            {
                TraceLog.Info($"[auth] TryResumeSessionAsync → not auto-resumable (userIdNull={snapshot.UserId is null}, locked={snapshot.IsLocked}, firstRun={snapshot.IsFirstRun}).", "auth");
                return new AuthOutcome(false, null, null);
            }

            // Mirror the sign-in post-steps so the shell, which is built the instant this returns, has authority (B04c).
            TraceLog.Info("[auth] TryResumeSessionAsync → resuming; warming capability gate + live access.", "auth");
            await gate.RefreshAsync(ct);
            if (liveAccess is not null) await liveAccess.RefreshAsync(ct);
            TraceLog.Info($"[auth] TryResumeSessionAsync ← gate after warm-up: {gate.DescribeState()}", "auth");
            return new AuthOutcome(true, null, snapshot.DisplayName);
        }
        catch (OperationCanceledException)
        {
            return new AuthOutcome(false, null, null);
        }
        catch (Exception ex)
        {
            gate.Clear(); // fail closed on any unexpected store/dispatch error
            TraceLog.Error("[auth] TryResumeSessionAsync threw → gate CLEARED (read-only).", ex, "auth");
            return new AuthOutcome(false, null, null);
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
            request.Password,
            request.ServerUserId);
        var result = await dispatcher.SendAsync(command, context.Value!, ct);
        if (!result.IsSuccess)
        {
            gate.Clear();
            TraceLog.Warn($"[auth] CompleteFirstRunAsync → dispatcher REFUSED ({result.Error}); gate CLEARED (read-only).", "auth");
            return new AuthOutcome(false, result.Error.Code, null);
        }

        // First-run opens the Manager shell without a later sign-in, so warm the gate here too (B04c).
        TraceLog.Info("[auth] CompleteFirstRunAsync → success; warming capability gate + live access.", "auth");
        await gate.RefreshAsync(ct);
        if (liveAccess is not null) await liveAccess.RefreshAsync(ct);
        TraceLog.Info($"[auth] CompleteFirstRunAsync ← gate after warm-up: {gate.DescribeState()}", "auth");
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

        var command = new ProvisionLocalMirrorUserCommand(request.Username, request.Username, request.Password, role,
            request.ServerUserId);
        var result = await dispatcher.SendAsync(command, context.Value!, ct);
        return result.IsSuccess
            ? new AuthOutcome(true, null, request.Username)
            : new AuthOutcome(false, result.Error.Code, null);
    }

    private static (IDispatcher Dispatcher, ICurrentUserProvider Provider) Resolve(AsyncServiceScope scope) =>
        (scope.ServiceProvider.GetRequiredService<IDispatcher>(),
            scope.ServiceProvider.GetRequiredService<ICurrentUserProvider>());
}
