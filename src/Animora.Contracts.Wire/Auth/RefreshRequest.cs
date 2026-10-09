namespace Animora.Contracts.Wire.Auth;

/// <summary>
/// Body of <c>POST /auth/refresh</c> (S04). Presenting an already-rotated token revokes the whole
/// token family, rotates the user's security stamp and returns <c>SESSION_EXPIRED</c> (reuse detection).
/// </summary>
/// <param name="RefreshToken">The opaque refresh token issued by a prior login/refresh.</param>
public sealed record RefreshRequest(string RefreshToken);
