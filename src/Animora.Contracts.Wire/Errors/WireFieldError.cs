namespace Animora.Contracts.Wire.Errors;

/// <summary>One field-level validation failure inside a <see cref="WireApiError"/>.</summary>
/// <param name="Field">The offending field name.</param>
/// <param name="Code">The catalog code for the rule that failed.</param>
public sealed record WireFieldError(string Field, string Code);
