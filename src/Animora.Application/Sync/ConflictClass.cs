namespace Animora.Application.Sync;

/// <summary>The data classes of ARCHITECTURE.md §8.5; each has exactly one conflict-resolution policy.</summary>
public enum ConflictClass
{
    /// <summary>UI preferences / display settings — the only class where last-write-wins is allowed.</summary>
    UiPreference = 0,

    /// <summary>Posted invoice / payment — immutable; a change is a reversal or compensating entry, never in place.</summary>
    PostedFinancial = 1,

    /// <summary>Ledger and audit — append-only forever; by construction cannot conflict.</summary>
    AppendOnlyLedger = 2,

    /// <summary>Inventory movements — both apply (additive convergence), on-hand is re-derived.</summary>
    InventoryMovement = 3,

    /// <summary>Appointment slot — domain validation; a colliding local intent is rejected, not merged.</summary>
    AppointmentSlot = 4,

    /// <summary>Owner/pet demographics (and the safe default) — explicit field-level merge, never whole-row LWW.</summary>
    Demographics = 5,
}
