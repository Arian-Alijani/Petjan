namespace Animora.Domain.Clinical;

/// <summary>
/// One radiology study recorded on a visit (X-ray, ultrasound, ...). Immutable snapshot stored in the
/// visit's JSON <c>Radiology</c> column and replicated with the visit.
/// </summary>
/// <param name="Id">ULID identifying the record inside its visit.</param>
/// <param name="TakenUtc">When the study was taken (the date the doctor picked), UTC.</param>
/// <param name="BodyPart">The organ / body part imaged.</param>
/// <param name="Result">The reading / findings text; <see langword="null"/> when not written yet.</param>
/// <param name="Attachments">Image / document files attached to the study (content-addressed).</param>
public sealed record RadiologyRecord(
    string Id,
    DateTimeOffset TakenUtc,
    string BodyPart,
    string? Result,
    IReadOnlyList<RadiologyAttachment> Attachments);

/// <summary>A file attached to a <see cref="RadiologyRecord"/>; the bytes live in the content-addressed file store.</summary>
/// <param name="FileHash">Lowercase hex SHA-256 of the content (the <c>ILocalFileStore</c> key).</param>
/// <param name="FileName">Original file name, for display.</param>
/// <param name="ContentType">MIME type, e.g. <c>image/png</c>.</param>
public sealed record RadiologyAttachment(string FileHash, string FileName, string ContentType);
