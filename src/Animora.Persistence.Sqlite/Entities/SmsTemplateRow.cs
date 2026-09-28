using Animora.Domain.Primitives;

namespace Animora.Persistence.Sqlite.Entities;

/// <summary>Seeded local reference templates. Rendering is local; no message is delivered.</summary>
[LocalOnly]
public sealed class SmsTemplateRow
{
    private SmsTemplateRow() { }

    public string Id { get; private set; } = string.Empty;
    public string TenantId { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string Channel { get; private set; } = string.Empty;
    public string Body { get; private set; } = string.Empty;

    public static SmsTemplateRow Create(string id, string tenantId, string name, string channel, string body)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);
        ArgumentException.ThrowIfNullOrWhiteSpace(body);
        return new SmsTemplateRow { Id = id, TenantId = tenantId, Name = name, Channel = channel, Body = body };
    }
}
