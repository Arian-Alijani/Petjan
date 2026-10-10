namespace Animora.Persistence.PostgreSql.Files;

/// <summary>Tenant-owned metadata, deliberately not replicated as a SyncableEntity.</summary>
public sealed class FileObject
{
    public string TenantId { get; set; } = "";
    public string Id { get; set; } = "";
    public string? Sha256 { get; set; }
    public long SizeBytes { get; set; }
    public long DeclaredSizeBytes { get; set; }
    public string ContentType { get; set; } = "";
    public string OriginalName { get; set; } = "";
    public FileState State { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
    public string CreatedByUserId { get; set; } = "";
    public string? UploadId { get; set; }
}
