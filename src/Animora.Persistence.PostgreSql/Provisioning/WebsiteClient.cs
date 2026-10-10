namespace Animora.Persistence.PostgreSql.Provisioning;

public enum WebsiteClientStatus : short { Active = 0, Revoked = 1 }
public sealed class WebsiteClient
{
    public string Id { get; set; } = string.Empty;
    public string KeyId { get; set; } = string.Empty;
    public byte[] ProtectedSecret { get; set; } = [];
    public WebsiteClientStatus Status { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset? LastUsedUtc { get; set; }
    public string Label { get; set; } = string.Empty;
}
