namespace Animora.Persistence.PostgreSql.Files;

public sealed class BlobRef
{
    public string Sha256 { get; set; } = "";
    public int RefCount { get; set; }
    public long SizeBytes { get; set; }
    public DateTimeOffset FirstSeenUtc { get; set; }
    // Reset on the last dereference, so old blobs receive the full GC grace period.
    public DateTimeOffset? UnreferencedUtc { get; set; }
}
