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

    /// <summary>
    /// «مرا به خاطر بسپار» startup path: restores the persisted local session WITHOUT a password. Returns a
    /// successful outcome only when a usable session was resumed — i.e. signed in, not locked (an idle session
    /// resumes locked and must NOT auto-open the shell), and not a fresh install. Side effects mirror a normal
    /// sign-in (session-context cache populated, capability gate warmed) so the shell can be built immediately.
    /// Any failure (no session, locked, expired, store error) returns an unsuccessful outcome and never throws.
    /// </summary>
    Task<AuthOutcome> TryResumeSessionAsync(
        CancellationToken ct = default);

    Task<bool> IsFirstRunAsync(
        CancellationToken ct = default);

    Task<AuthOutcome> CompleteFirstRunAsync(
        FirstRunRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Ensures a local credential mirror (user + active membership of the server role) exists for a user the central
    /// server has just authenticated, so the local session can be opened for server-created, non-owner staff.
    /// Idempotent; fails closed with <c>SESSION_EXPIRED</c> unless a matching online session is stored.
    /// </summary>
    Task<AuthOutcome> EnsureLocalMirrorAsync(
        ServerIdentityMirrorRequest request,
        CancellationToken ct = default);
}
