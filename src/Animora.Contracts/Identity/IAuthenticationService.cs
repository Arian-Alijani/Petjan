namespace Animora.Contracts.Identity;

/// <summary>
/// Desktop-facing local authentication seam (B03c). Failures carry a stable error code only — never
/// whether the username exists (CONVENTIONS §7). Code → Persian text mapping lives in the Desktop.
/// </summary>
public interface IAuthenticationService
{
    Task<AuthOutcome> SignInAsync(
        string username,
        string password,
        bool rememberDevice,
        CancellationToken ct = default);

    Task SignOutAsync(
        string reason,
        CancellationToken ct = default);

    Task<bool> IsFirstRunAsync(
        CancellationToken ct = default);

    Task<AuthOutcome> CompleteFirstRunAsync(
        FirstRunRequest request,
        CancellationToken ct = default);
}
