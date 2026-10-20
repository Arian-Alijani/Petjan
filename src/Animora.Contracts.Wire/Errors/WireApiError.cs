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
/// <param name="Category">
/// CW-04: the domain <c>ErrorCode.Category</c> (Validation/NotFound/Conflict/Forbidden/Integrity/
/// Infrastructure/Entitlement) carried authoritatively from the server. Optional &amp; nullable so older
/// peers still deserialize and v1 producers can omit it; a client prefers this when present and falls back
/// to its own local catalog derivation when <see langword="null"/>.
/// </param>
public sealed record WireApiError(
    string Code,
    string? Detail,
    IReadOnlyList<WireFieldError> Fields,
    string CorrelationId,
    string? Category = null);
