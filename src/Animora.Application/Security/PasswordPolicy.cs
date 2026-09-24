using Animora.Domain.Results;

namespace Animora.Application.Security;

/// <summary>Fixed local password policy (B03): at least 8 characters, one letter and one digit.</summary>
public static class PasswordPolicy
{
    public const int MinLength = 8;

    public static Result Validate(string? password)
    {
        if (password is null || password.Length < MinLength)
        {
            return Result.Fail(ErrorCodes.PasswordPolicyViolation, $"Password must be at least {MinLength} characters.");
        }

        var hasLetter = false;
        var hasDigit = false;
        foreach (var c in password)
        {
            hasLetter |= char.IsLetter(c);
            hasDigit |= char.IsDigit(c);
        }

        return hasLetter && hasDigit
            ? Result.Ok()
            : Result.Fail(ErrorCodes.PasswordPolicyViolation, "Password must contain at least one letter and one digit.");
    }
}
