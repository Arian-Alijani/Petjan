using Animora.Application.Security;
using Microsoft.AspNetCore.Identity;
using IdentityOptions = Microsoft.Extensions.Options.Options;

namespace Animora.Infrastructure.Local.Security;

/// <summary><see cref="IPasswordHasher"/> over ASP.NET Core Identity's PBKDF2 (IdentityV3) hasher — no custom crypto.</summary>
public sealed class PasswordHashingService : Application.Security.IPasswordHasher
{
    public const int IterationCount = 210_000;

    private static readonly UserHashSubject Subject = new();

    private readonly PasswordHasher<UserHashSubject> _hasher = new(IdentityOptions.Create(new PasswordHasherOptions
    {
        CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3,
        IterationCount = IterationCount,
    }));

    public string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        return _hasher.HashPassword(Subject, password);
    }

    public PasswordVerificationOutcome Verify(string hash, string password)
    {
        if (string.IsNullOrEmpty(hash) || string.IsNullOrEmpty(password)) return PasswordVerificationOutcome.Failed;
        try
        {
            return _hasher.VerifyHashedPassword(Subject, hash, password) switch
            {
                PasswordVerificationResult.Success => PasswordVerificationOutcome.Success,
                PasswordVerificationResult.SuccessRehashNeeded => PasswordVerificationOutcome.SuccessRehashNeeded,
                _ => PasswordVerificationOutcome.Failed,
            };
        }
        catch (FormatException)
        {
            return PasswordVerificationOutcome.Failed; // corrupt stored hash: fail closed.
        }
    }

    /// <summary>The hasher's generic subject is unused by the algorithm.</summary>
    private sealed class UserHashSubject;
}
