namespace Animora.Application.Security;

/// <summary>Result of verifying a password against a stored hash.</summary>
public enum PasswordVerificationOutcome
{
    Failed,
    Success,

    /// <summary>Correct password, but the hash uses outdated parameters; the caller re-hashes it.</summary>
    SuccessRehashNeeded,
}

/// <summary>Password hashing port (CONVENTIONS §7: implemented over ASP.NET Core Identity primitives only).</summary>
public interface IPasswordHasher
{
    string Hash(string password);

    PasswordVerificationOutcome Verify(string hash, string password);
}
