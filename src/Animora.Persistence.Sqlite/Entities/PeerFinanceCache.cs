using Animora.Domain.Primitives;
using Animora.Domain.Sales;

namespace Animora.Persistence.Sqlite.Entities;

/// <summary>
/// D04b: a <b>read-only, server-authored</b> copy of an invoice another device (or the server) produced, fetched from
/// <c>GET /finance/ledger</c>. Invoices/payments are command-settled money and are deliberately excluded from the
/// generic state feed, so this cache is the only way a peer can display a cross-device transaction.
/// <para>
/// <see cref="LocalOnlyAttribute">Local-only</see> and never written by a command handler, never read by the command
/// pipeline: it is a display projection. The id is the server invoice id, which equals the creating operation id — so
/// an invoice this device also produced coincides with its own <c>Invoices</c> row and the read models dedup by id.
/// </para>
/// </summary>
[LocalOnly]
public sealed class PeerInvoice
{
    private PeerInvoice() { }

    /// <summary>Server invoice id (the dedup/upsert key; equals the local invoice id for an invoice this device created).</summary>
    public string Id { get; private set; } = string.Empty;

    /// <summary>Owning tenant.</summary>
    public string TenantId { get; private set; } = string.Empty;

    /// <summary>Authoritative invoice number allocated by the server.</summary>
    public string Number { get; private set; } = string.Empty;

    /// <summary>Invoice kind.</summary>
    public InvoiceKind Kind { get; private set; }

    /// <summary>Invoice state as the server last reported it.</summary>
    public InvoiceState State { get; private set; }

    /// <summary>Owner id, when attributed to an owner.</summary>
    public string? OwnerId { get; private set; }

    /// <summary>Owner full name, or the guest name for a walk-in.</summary>
    public string PartyName { get; private set; } = string.Empty;

    /// <summary>Pet name, when known.</summary>
    public string PetName { get; private set; } = string.Empty;

    /// <summary>When the invoice was issued.</summary>
    public DateTimeOffset IssuedUtc { get; private set; }

    /// <summary>When the invoice was posted.</summary>
    public DateTimeOffset? PostedUtc { get; private set; }

    /// <summary>Grand total, minor units.</summary>
    public long GrandTotalMinor { get; private set; }

    /// <summary>Collected so far, minor units.</summary>
    public long PaidTotalMinor { get; private set; }

    /// <summary>Outstanding balance, minor units.</summary>
    public long DueTotalMinor { get; private set; }

    /// <summary>The server's effective last-change tick; a stale re-delivery never overwrites a newer row.</summary>
    public long EffectiveTicks { get; private set; }

    /// <summary>Full immutable detail, JSON encoded; null for legacy summary-only servers.</summary>
    public string? DetailJson { get; private set; }

    /// <summary>Creates a cache row from one wire row.</summary>
    public static PeerInvoice From(string tenantId, Animora.Contracts.Wire.Sync.FinanceInvoiceRow row)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentNullException.ThrowIfNull(row);
        var created = new PeerInvoice { Id = row.Id, TenantId = tenantId };
        created.Overwrite(row);
        return created;
    }

    /// <summary>Overwrites this row with a wire row that is at least as new (older deliveries are ignored by the caller).</summary>
    public void Overwrite(Animora.Contracts.Wire.Sync.FinanceInvoiceRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (row.Detail is not null) DetailJson = System.Text.Json.JsonSerializer.Serialize(row.Detail);
        Number = row.Number;
        Kind = (InvoiceKind)row.Kind;
        State = (InvoiceState)row.State;
        OwnerId = string.IsNullOrWhiteSpace(row.OwnerId) ? null : row.OwnerId;
        PartyName = row.PartyName ?? string.Empty;
        PetName = row.PetName ?? string.Empty;
        IssuedUtc = row.IssuedUtc;
        PostedUtc = row.PostedUtc;
        GrandTotalMinor = row.GrandTotalMinor;
        PaidTotalMinor = row.PaidTotalMinor;
        DueTotalMinor = row.DueTotalMinor;
        EffectiveTicks = row.EffectiveTicks;
    }
}

/// <summary>D04b: a read-only, server-authored payment (or refund, when negative) belonging to a <see cref="PeerInvoice"/>.</summary>
[LocalOnly]
public sealed class PeerPayment
{
    private PeerPayment() { }

    /// <summary>Server payment id (dedup/upsert key).</summary>
    public string Id { get; private set; } = string.Empty;

    /// <summary>Owning tenant.</summary>
    public string TenantId { get; private set; } = string.Empty;

    /// <summary>The invoice this payment settles.</summary>
    public string InvoiceId { get; private set; } = string.Empty;

    /// <summary>Payment method.</summary>
    public PaymentMethod Method { get; private set; }

    /// <summary>Signed amount in minor units (negative ⇒ refund).</summary>
    public long AmountMinor { get; private set; }

    /// <summary>When the money moved.</summary>
    public DateTimeOffset ReceivedUtc { get; private set; }

    /// <summary>Creates a cache row from one wire row.</summary>
    public static PeerPayment From(string tenantId, Animora.Contracts.Wire.Sync.FinancePaymentRow row)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentNullException.ThrowIfNull(row);
        var created = new PeerPayment { Id = row.Id, TenantId = tenantId };
        created.Overwrite(row);
        return created;
    }

    /// <summary>Overwrites this row with the wire row (payments are immutable server-side; this is idempotent).</summary>
    public void Overwrite(Animora.Contracts.Wire.Sync.FinancePaymentRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        InvoiceId = row.InvoiceId;
        Method = (PaymentMethod)row.Method;
        AmountMinor = row.AmountMinor;
        ReceivedUtc = row.ReceivedUtc;
    }
}

/// <summary>
/// D04b: where this device resumes polling <c>GET /finance/ledger</c> — one row per tenant, the server's
/// <c>NextCursorTicks</c> of the last fully applied page. <see cref="LocalOnlyAttribute">Local-only</see>; losing it only means one
/// idempotent full re-read.
/// </summary>
[LocalOnly]
public sealed class PeerFinanceCursor
{
    private PeerFinanceCursor() { }

    /// <summary>Owning tenant (primary key).</summary>
    public string TenantId { get; private set; } = string.Empty;

    /// <summary>The exclusive-resume UTC tick returned by the last applied ledger page.</summary>
    public long CursorTicks { get; private set; }

    /// <summary>Creates the cursor row.</summary>
    public static PeerFinanceCursor Create(string tenantId, long cursorTicks)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        return new PeerFinanceCursor { TenantId = tenantId, CursorTicks = Math.Max(0, cursorTicks) };
    }

    /// <summary>Moves the cursor forward only; a stale/rewound page never lowers it.</summary>
    public void Advance(long cursorTicks) => CursorTicks = Math.Max(CursorTicks, cursorTicks);
}
