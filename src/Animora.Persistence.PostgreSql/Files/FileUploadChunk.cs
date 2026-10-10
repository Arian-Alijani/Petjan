namespace Animora.Persistence.PostgreSql.Files;

public sealed class FileUploadChunk
{
    public string TenantId { get; set; } = "";
    public string UploadId { get; set; } = "";
    public long Offset { get; set; }
    public int Length { get; set; }
}
