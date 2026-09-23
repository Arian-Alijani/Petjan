using Animora.Domain.Primitives;
using Animora.Domain.Results;
using Animora.Domain.Time;

namespace Animora.Domain.Patients;

/// <summary>What a stored document is.</summary>
public enum DocumentKind
{
    /// <summary>Laboratory report.</summary>
    Lab = 0,

    /// <summary>Radiograph, ultrasound or other imaging.</summary>
    Imaging = 1,

    /// <summary>Prescription document.</summary>
    Prescription = 2,

    /// <summary>Signed consent form.</summary>
    Consent = 3,

    /// <summary>Invoice or receipt scan.</summary>
    Invoice = 4,

    /// <summary>Anything else.</summary>
    Other = 5,
}

/// <summary>
/// A pointer from clinical data to a stored file. It holds the <b>file-metadata id</b> issued by the
/// file store (D12) plus the content hash — <b>never a filesystem path</b> (A-RULE 020): a path is
/// machine-local, breaks on sync, and leaks the storage layout into the domain.
/// </summary>
/// <remarks>
/// At least one of <see cref="OwnerId"/> / <see cref="PetId"/> / <see cref="VisitId"/> must be set,
/// otherwise the document is unreachable from every screen; the factory enforces that with
/// <c>DOCUMENT_TARGET_REQUIRED</c>.
/// </remarks>
public sealed class DocumentReference : SyncableEntity
{
    /// <summary>EF Core materialization constructor. Not for business code.</summary>
    private DocumentReference()
    {
    }

    /// <summary>Owner the document belongs to, when it is client-level.</summary>
    public string? OwnerId { get; private init; }

    /// <summary>Patient the document belongs to, when it is patient-level.</summary>
    public string? PetId { get; private init; }

    /// <summary>Visit the document was produced in, when it is encounter-level.</summary>
    public string? VisitId { get; private init; }

    /// <summary>Operator-visible title.</summary>
    public string Title { get; private init; } = string.Empty;

    /// <summary>Document category.</summary>
    public DocumentKind Kind { get; private init; }

    /// <summary>Id of the file-metadata row in the file store (D12). Never a path.</summary>
    public string FileId { get; private init; } = string.Empty;

    /// <summary>Payload size in bytes, mirrored here so lists render without touching the store.</summary>
    public long SizeBytes { get; private init; }

    /// <summary>Lowercase hex SHA-256 of the payload; the integrity check on download.</summary>
    public string ContentHashSha256 { get; private init; } = string.Empty;

    /// <summary>When the file was uploaded, UTC.</summary>
    public DateTimeOffset UploadedUtc { get; private init; }

    /// <summary>The staff member who uploaded it.</summary>
    public string UploadedByUserId { get; private init; } = string.Empty;

    /// <summary>Creates a reference, requiring at least one attachment target.</summary>
    public static Result<DocumentReference> Attach(
        string tenantId,
        string title,
        DocumentKind kind,
        string fileId,
        long sizeBytes,
        string contentHashSha256,
        string uploadedByUserId,
        IClock clock,
        string? ownerId = null,
        string? petId = null,
        string? visitId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileId);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHashSha256);
        ArgumentException.ThrowIfNullOrWhiteSpace(uploadedByUserId);
        ArgumentOutOfRangeException.ThrowIfNegative(sizeBytes);
        ArgumentNullException.ThrowIfNull(clock);

        if (string.IsNullOrWhiteSpace(ownerId) &&
            string.IsNullOrWhiteSpace(petId) &&
            string.IsNullOrWhiteSpace(visitId))
        {
            return Result<DocumentReference>.Fail(
                ErrorCodes.DocumentTargetRequired,
                "A document must reference at least one of OwnerId, PetId or VisitId.");
        }

        var now = clock.UtcNow;
        return Result<DocumentReference>.Ok(new DocumentReference
        {
            Id = IdGenerator.NewId(now),
            TenantId = tenantId,
            OwnerId = Clean(ownerId),
            PetId = Clean(petId),
            VisitId = Clean(visitId),
            Title = title.Trim(),
            Kind = kind,
            FileId = fileId,
            SizeBytes = sizeBytes,
            ContentHashSha256 = contentHashSha256,
            UploadedUtc = now,
            UploadedByUserId = uploadedByUserId,
        });
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
