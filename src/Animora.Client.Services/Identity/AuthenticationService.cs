using Animora.Application.Abstractions;
using Animora.Application.Security;
using Animora.Application.UseCases.Identity;
using Animora.Contracts.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Animora.Client.Services.Identity;

/// <summary>
/// <see cref="IAuthenticationService"/> over the B03b identity use cases. Singleton: the scoped
/// <see cref="IDispatcher"/> and <see cref="ICurrentUserProvider"/> are resolved from a fresh async scope per
/// operation, never captured. Awaits intentionally do not use <c>ConfigureAwait(false)</c> (results return to a
/// ViewModel — CONVENTIONS §4).
/// </summary>
public sealed class AuthenticationService(IServiceScopeFactory scopes) : IAuthenticationService
{
    public async Task<AuthOutcome> SignInAsync(string username, string password, bool rememberDevice,
        CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var (dispatcher, provider) = Resolve(scope);

        var context = provider.BuildAnonymousCommandContext();
        if (context.IsFailure) return new AuthOutcome(false, context.Error.Code, null);

        var result = await dispatcher.SendAsync(new SignInCommand(username, password, rememberDevice), context.Value!, ct);
        return result.IsSuccess
            ? new AuthOutcome(true, null, result.Value!.DisplayName)
            : new AuthOutcome(false, result.Error.Code, null);
    }

    public async Task SignOutAsync(string reason, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var (dispatcher, provider) = Resolve(scope);

        var context = await provider.BuildCommandContextAsync(null, null, null, ct);
        if (context.IsFailure) return; // no active session: sign-out is idempotent.

        // Expected failures are not surfaced: the contract has no error channel for sign-out.
        await dispatcher.SendAsync(new SignOutCommand(reason), context.Value!, ct);
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
        if (context.IsFailure) return new AuthOutcome(false, context.Error.Code, null);

        var command = new CompleteFirstRunSetupCommand(
            request.ClinicName,
            request.OwnerFullName,
            request.Username,
            request.Password);
        var result = await dispatcher.SendAsync(command, context.Value!, ct);
        return result.IsSuccess
            ? new AuthOutcome(true, null, result.Value!.DisplayName)
            : new AuthOutcome(false, result.Error.Code, null);
    }

    private static (IDispatcher Dispatcher, ICurrentUserProvider Provider) Resolve(AsyncServiceScope scope) =>
        (scope.ServiceProvider.GetRequiredService<IDispatcher>(),
            scope.ServiceProvider.GetRequiredService<ICurrentUserProvider>());
}
