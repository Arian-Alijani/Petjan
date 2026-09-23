using Animora.Domain.Primitives;
using Animora.Domain.Time;
using Animora.Domain.ValueObjects;

namespace Animora.Domain.Identity;

/// <summary>Lifecycle state of a tenant (clinic).</summary>
public enum TenantStatus
{
    /// <summary>Normal operation.</summary>
    Active = 0,

    /// <summary>Temporarily blocked (billing/administrative); data is retained.</summary>
    Suspended = 1,

    /// <summary>Permanently closed; read-only archival state.</summary>
    Closed = 2,
}

/// <summary>
/// A clinic: the tenancy unit of the product (<c>ARCHITECTURE.md</c> §9.1). One shared
/// schema, one <c>TenantId</c> column on every replicated row — never a database per tenant.
/// </summary>
/// <remarks>
/// <see cref="SyncableEntity.Id"/> <b>is</b> the value every other entity carries in its
/// <see cref="SyncableEntity.TenantId"/>. The factory sets this tenant's own
/// <c>TenantId</c> equal to its <c>Id</c> so a single uniform tenant filter also covers the
/// tenant row itself (<c>CONVENTIONS.md</c> §5).
/// </remarks>
public sealed class Tenant : AggregateRoot
{
    /// <summary>The default IANA timezone of a clinic created without an explicit one.</summary>
    public const string DefaultTimezone = "Asia/Tehran";

    /// <summary>EF Core materialization constructor. Not for business code.</summary>
    private Tenant()
    {
    }

    /// <summary>Clinic name shown in the UI.</summary>
    public string DisplayName { get; private set; } = string.Empty;

    /// <summary>Registered legal entity name, when it differs from <see cref="DisplayName"/>.</summary>
    public string? LegalName { get; private set; }

    /// <summary>IANA timezone id used to interpret this clinic's business days.</summary>
    public string Timezone { get; private set; } = DefaultTimezone;

    /// <summary>ISO-4217 code of the currency all money of this tenant is stored in.</summary>
    public string DefaultCurrency { get; private set; } = CurrencyScale.Irt;

    /// <summary>Lifecycle state; anything other than <see cref="TenantStatus.Active"/> gates writes.</summary>
    public TenantStatus Status { get; private set; } = TenantStatus.Active;

    /// <summary>Creates an active tenant whose <c>TenantId</c> equals its own <c>Id</c>.</summary>
    public static Tenant Create(
        string displayName,
        IClock clock,
        string? legalName = null,
        string timezone = DefaultTimezone,
        string defaultCurrency = CurrencyScale.Irt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(timezone);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultCurrency);
        ArgumentNullException.ThrowIfNull(clock);

        var id = IdGenerator.NewId(clock.UtcNow);
        return new Tenant
        {
            Id = id,
            TenantId = id,
            DisplayName = displayName.Trim(),
            LegalName = string.IsNullOrWhiteSpace(legalName) ? null : legalName.Trim(),
            Timezone = timezone,
            DefaultCurrency = defaultCurrency,
            Status = TenantStatus.Active,
        };
    }

    /// <summary>Moves the tenant to <paramref name="status"/>. Idempotent.</summary>
    public void SetStatus(TenantStatus status) => Status = status;

    /// <summary>Renames the clinic.</summary>
    public void Rename(string displayName, string? legalName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        DisplayName = displayName.Trim();
        LegalName = string.IsNullOrWhiteSpace(legalName) ? LegalName : legalName.Trim();
    }
}
