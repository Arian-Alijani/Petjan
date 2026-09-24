using Animora.Application.Abstractions;
using Animora.Application.Dispatch;
using Animora.Application.Security;

namespace Animora.Application.UseCases.Identity;

/// <summary>
/// Registers the 7 identity use cases + validators. Called from the <c>AddAnimoraApplication</c> configure
/// lambda by the host that also registers the local store and <see cref="ISessionService"/> (Desktop, B03c) —
/// kept opt-in so hosts without identity infrastructure still validate their registry.
/// </summary>
public static class IdentityRegistration
{
    public static ApplicationRegistrationBuilder AddIdentityUseCases(this ApplicationRegistrationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder
            .AddCommand<SignInCommand, SignInResult, SignInCommandHandler>()
            .AddValidator<SignInCommand, SignInCommandValidator>()
            .AddCommand<SignOutCommand, Unit, SignOutCommandHandler>()
            .AddCommand<LockSessionCommand, Unit, LockSessionCommandHandler>()
            .AddCommand<UnlockSessionCommand, SessionSnapshot, UnlockSessionCommandHandler>()
            .AddValidator<UnlockSessionCommand, UnlockSessionCommandValidator>()
            .AddCommand<ChangePasswordCommand, Unit, ChangePasswordCommandHandler>()
            .AddValidator<ChangePasswordCommand, ChangePasswordCommandValidator>()
            .AddCommand<CompleteFirstRunSetupCommand, SignInResult, CompleteFirstRunSetupCommandHandler>()
            .AddValidator<CompleteFirstRunSetupCommand, CompleteFirstRunSetupCommandValidator>()
            .AddQuery<CurrentSessionQuery, SessionSnapshot, CurrentSessionQueryHandler>();
    }
}
