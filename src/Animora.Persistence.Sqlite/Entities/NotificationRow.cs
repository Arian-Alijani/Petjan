using Animora.Domain.Primitives;

namespace Animora.Persistence.Sqlite.Entities;

/// <summary>
/// One message of the local notifications feed. Device-local (<see cref="LocalOnlyAttribute"/>): the vendor server is
/// not wired to deliver messages yet, so an admin write lands here and is visible immediately with no sync. LocalOnly
/// rows carry no global tenant filter, so every query must predicate on <see cref="TenantId"/> explicitly.
/// <c>AudienceKind</c>/<c>Kind</c> store the numeric values of the Contracts enums.
/// </summary>
[LocalOnly]
public sealed class NotificationRow
{
    /// <summary>Upper bound of <see cref="Body"/> (also enforced by the service).</summary>
    public const int BodyMaxLength = 2000;

    private NotificationRow() { }

    /// <summary>ULID primary key.</summary>
    public string Id { get; private set; } = string.Empty;

    /// <summary>Owning tenant (explicit predicate in every query).</summary>
    public string TenantId { get; private set; } = string.Empty;

    /// <summary>0 = all users, 1 = a staff role, 2 = one user.</summary>
    public int AudienceKind { get; private set; }

    /// <summary>Role name (<c>AudienceKind</c> 1) or user id (2); null for everyone.</summary>
    public string? AudienceValue { get; private set; }

    /// <summary>Semantic kind: info / success / warning / promo / system / reward.</summary>
    public int Kind { get; private set; }

    /// <summary>Optional short label shown as a coloured chip.</summary>
    public string? Tag { get; private set; }

    /// <summary>Headline.</summary>
    public string Title { get; private set; } = string.Empty;

    /// <summary>Message text.</summary>
    public string Body { get; private set; } = string.Empty;

    /// <summary>The author (the admin, or the signed-in user that triggered a system notice).</summary>
    public string CreatedByUserId { get; private set; } = string.Empty;

    /// <summary>When the row was written.</summary>
    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <summary>Planned publish time of a scheduled message; null when sent immediately.</summary>
    public DateTimeOffset? ScheduledForUtc { get; private set; }

    /// <summary>When the message became visible; null while it waits for its schedule.</summary>
    public DateTimeOffset? PublishedAtUtc { get; private set; }

    /// <summary>When the message stops being shown; null = never.</summary>
    public DateTimeOffset? ExpiresAtUtc { get; private set; }

    /// <summary>
    /// Idempotency key of automatic notices (e.g. <c>system:sub-expiry:20261201</c>); null for hand-written messages.
    /// Unique per tenant, so a notice is issued once however often the issuing check runs.
    /// </summary>
    public string? DedupKey { get; private set; }

    /// <summary>Creates a message; a null <paramref name="publishedAtUtc"/> keeps it scheduled.</summary>
    public static NotificationRow Create(string tenantId, int audienceKind, string? audienceValue, int kind, string? tag,
        string title, string body, string createdByUserId, DateTimeOffset createdAtUtc, DateTimeOffset? scheduledForUtc,
        DateTimeOffset? publishedAtUtc, DateTimeOffset? expiresAtUtc, string? dedupKey = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(body);
        ArgumentException.ThrowIfNullOrWhiteSpace(createdByUserId);
        return new()
        {
            Id = IdGenerator.NewId(),
            TenantId = tenantId,
            AudienceKind = audienceKind,
            AudienceValue = string.IsNullOrWhiteSpace(audienceValue) ? null : audienceValue.Trim(),
            Kind = kind,
            Tag = string.IsNullOrWhiteSpace(tag) ? null : tag.Trim(),
            Title = title.Trim(),
            Body = body.Trim(),
            CreatedByUserId = createdByUserId,
            CreatedAtUtc = createdAtUtc,
            ScheduledForUtc = scheduledForUtc,
            PublishedAtUtc = publishedAtUtc,
            ExpiresAtUtc = expiresAtUtc,
            DedupKey = string.IsNullOrWhiteSpace(dedupKey) ? null : dedupKey,
        };
    }

    /// <summary>Author id stamped on rows delivered from the vendor server (the admin panel operator is not a clinic user).</summary>
    public const string VendorAuthor = "vendor";

    /// <summary>
    /// Materialises a vendor message delivered by the server pull, KEEPING the server's stable id (the idempotent upsert
    /// key across devices and re-pulls). A message with no future schedule is visible on arrival (published at its creation
    /// time); one scheduled for later stays unpublished until the local due-sweep publishes it.
    /// </summary>
    public static NotificationRow FromServer(string id, string tenantId, int audienceKind, string? audienceValue, int kind,
        string? tag, string title, string body, DateTimeOffset createdAtUtc, DateTimeOffset? scheduledForUtc,
        DateTimeOffset? expiresAtUtc, DateTimeOffset nowUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var row = Create(tenantId, audienceKind, audienceValue, kind, tag, title, body, VendorAuthor, createdAtUtc,
            scheduledForUtc, scheduledForUtc is { } at && at > nowUtc ? null : (scheduledForUtc ?? createdAtUtc), expiresAtUtc);
        row.Id = id;
        return row;
    }

    /// <summary>Makes a scheduled message visible (idempotent: the first publish time wins).</summary>
    public void Publish(DateTimeOffset nowUtc) => PublishedAtUtc ??= nowUtc;
}
