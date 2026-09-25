using Animora.Application.Validation;

namespace Animora.Application.UseCases.Identity;

/// <summary>Shared field limits and rule composition for the identity use cases.</summary>
internal static class IdentityValidation
{
    public const int UsernameMax = 128;
    public const int PasswordMax = 256;
    public const int NameMax = 200;

    public static ValueTask<IReadOnlyList<ValidationFailure>> Collect(params ValidationFailure?[] failures) =>
        ValueTask.FromResult<IReadOnlyList<ValidationFailure>>(failures.OfType<ValidationFailure>().ToArray());

    public static ValidationFailure? RequiredMax(string? value, int max, string field) =>
        Rules.Required(value, field) ?? Rules.MaxLength(value, max, field);
}
