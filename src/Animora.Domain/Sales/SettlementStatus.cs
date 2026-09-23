namespace Animora.Domain.Sales;

/// <summary>
/// The lifecycle of a money-facing command as it travels from the device that produced it to the
/// server that owns the truth (ARCHITECTURE.md §8.3). Defined <b>once</b>, here, and reused by the
/// command envelope (B02), the sync pipeline (D05) and the settlement UX (E06) — never redefined.
/// </summary>
/// <remarks>
/// The status is carried by <see cref="Invoice"/> and <see cref="Payment"/> so the UI can always
/// show it: a command that cannot settle is never silently swallowed (sync-failure UX contract,
/// ARCHITECTURE.md §8.3).
/// </remarks>
public enum SettlementStatus
{
    /// <summary>Not submitted yet; still being composed locally.</summary>
    Draft = 0,

    /// <summary>Committed to the local store; the device treats it as real, the server has not seen it.</summary>
    LocallyPosted = 1,

    /// <summary>Handed to the command queue; awaiting the server's verdict.</summary>
    PendingSettlement = 2,

    /// <summary>Accepted by the server. Terminal, and the only success state.</summary>
    Settled = 3,

    /// <summary>Refused by the server (validation, entitlement, permission). Needs a human decision.</summary>
    Rejected = 4,

    /// <summary>Settled against a state the device did not have; a domain conflict policy must resolve it.</summary>
    Conflict = 5,

    /// <summary>Its financial effect was undone by a compensating entry (A-RULE 002/003).</summary>
    Reversed = 6,

    /// <summary>Stuck in a way no automatic rule can clear; explicitly surfaced for staff action.</summary>
    NeedsAttention = 7,
}
