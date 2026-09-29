using Animora.Domain.Primitives;

namespace Animora.Persistence.Sqlite.Entities;

/// <summary>
/// C10: one materialized row of the unified activity feed (History screen). Written from the B02
/// outbox by <c>ActivityFeedHandler</c> for the 14 mapped domain-event types, and reproducible from
/// source tables by <c>ILocalStoreMaintenance.RebuildActivityFeedAsync</c> (a cache with a proof —
/// A-RULE 018/034). Never scanned across the OLTP write tables at query time.
/// <para>
/// <b>Localization.</b> <see cref="Title"/> is a localization <i>key</i> (e.g.
/// <c>history.invoice.posted</c>, ASCII only) and <see cref="DetailArgsJson"/> its JSON arguments;
/// Persian rendering happens only in the Desktop <c>HistoryText</c> (CONVENTIONS §4).
/// </para>
/// <para>
/// <b>Kind/Status are stored as strings</b> (the enum names) because Persistence does not reference
/// <c>Animora.Contracts</c>; the Application read model parses them back to the contract enums.
/// </para>
/// </summary>
[LocalOnly]
public sealed class ActivityEvent
{
    private ActivityEvent() { }

    /// <summary>ULID primary key.</summary>
    public string Id { get; private set; } = string.Empty;

    /// <summary>Owning tenant (filtered manually — LocalOnly rows carry no global query filter).</summary>
    public string TenantId { get; private set; } = string.Empty;

    /// <summary>When the underlying event occurred (UTC).</summary>
    public DateTimeOffset OccurredUtc { get; private set; }

    /// <summary>Clinic-local calendar day as <c>yyyymmdd</c> (matches <c>DailyFinanceFact.LocalDay</c>).</summary>
    public int LocalDay { get; private set; }

    /// <summary>The <c>HistoryEventKind</c> enum <i>name</i> (e.g. "Sale", "Invoice"→mapped kind).</summary>
    public string Kind { get; private set; } = string.Empty;

    /// <summary>Localization key for the primary line (ASCII only, e.g. "history.invoice.posted").</summary>
    public string Title { get; private set; } = string.Empty;

    /// <summary>JSON arguments for the localized detail line (e.g. {"number":"04-000123"}); may be empty.</summary>
    public string DetailArgsJson { get; private set; } = string.Empty;

    /// <summary>Source aggregate type tag (e.g. "Invoice", "Pet", "Cheque").</summary>
    public string EntityType { get; private set; } = string.Empty;

    /// <summary>Source aggregate id.</summary>
    public string EntityId { get; private set; } = string.Empty;

    /// <summary>Absolute money magnitude in minor units; 0 when the event carries no money.</summary>
    public long AmountMinor { get; private set; }

    /// <summary>+1 income, -1 expense, 0 no amount.</summary>
    public int AmountSign { get; private set; }

    /// <summary>The <c>HistoryEventStatus</c> enum <i>name</i> (e.g. "Paid", "Done").</summary>
    public string Status { get; private set; } = string.Empty;

    /// <summary>User who performed the action (audit "who"); may be empty for system events.</summary>
    public string ActorUserId { get; private set; } = string.Empty;

    /// <summary>Display name of the actor at the time (denormalized for the feed).</summary>
    public string ActorName { get; private set; } = string.Empty;

    /// <summary>Correlation id linking this row to its originating command (may be null).</summary>
    public string? CorrelationId { get; private set; }

    /// <summary>
    /// Appends one feed row. Called by the writer inside the caller's transaction (never SaveChanges).
    /// </summary>
    public static ActivityEvent Create(
        string tenantId,
        DateTimeOffset occurredUtc,
        int localDay,
        string kind,
        string title,
        string detailArgsJson,
        string entityType,
        string entityId,
        long amountMinor,
        int amountSign,
        string status,
        string actorUserId,
        string actorName,
        string? correlationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        if (amountMinor < 0) throw new ArgumentOutOfRangeException(nameof(amountMinor));
        if (amountSign is < -1 or > 1) throw new ArgumentOutOfRangeException(nameof(amountSign));
        return new()
        {
            Id = IdGenerator.NewId(occurredUtc),
            TenantId = tenantId,
            OccurredUtc = occurredUtc,
            LocalDay = localDay,
            Kind = kind,
            Title = title,
            DetailArgsJson = detailArgsJson ?? string.Empty,
            EntityType = entityType ?? string.Empty,
            EntityId = entityId ?? string.Empty,
            AmountMinor = amountMinor,
            AmountSign = amountSign,
            Status = status ?? string.Empty,
            ActorUserId = actorUserId ?? string.Empty,
            ActorName = actorName ?? string.Empty,
            CorrelationId = correlationId,
        };
    }
}
