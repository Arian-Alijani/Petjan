namespace Animora.Contracts.Wire.Errors;

/// <summary>
/// The <b>only</b> error shape crossing the wire (ARCHITECTURE.md §8.6). Every API failure — validation,
/// authorization, conflict, infrastructure — is projected into this one record so a client never has to
/// parse a transport-specific error body.
/// </summary>
/// <param name="Code">Catalog error code (a stable string, never a localized message).</param>
/// <param name="Detail">Developer-facing diagnostic string; never user-facing localized text.</param>
/// <param name="Fields">Field-level failures; empty when the error is not field-specific.</param>
/// <param name="CorrelationId">The correlation id, so a client report can be tied to server logs.</param>
public sealed record WireApiError(
    string Code,
    string? Detail,
    IReadOnlyList<WireFieldError> Fields,
    string CorrelationId);
