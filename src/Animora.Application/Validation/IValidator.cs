namespace Animora.Application.Validation;

/// <summary>
/// Validates one request type. Registered explicitly via
/// <c>ApplicationRegistrationBuilder.AddValidator</c>. Returns failures — never throws for invalid input.
/// </summary>
public interface IValidator<in T>
{
    ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(T instance, CancellationToken ct);
}
