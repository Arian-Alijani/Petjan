using System.Text.Json;
using Animora.Contracts.Wire.Commands;
using Animora.Contracts.Wire.Sync;
using Animora.Domain.Results;

namespace Animora.Application.Sync;

/// <summary>
/// Encodes ARCHITECTURE.md §8.5: a version mismatch <i>detects</i> a conflict, domain policy <i>decides</i>. Pure — no EF, no HTTP, no clock,
/// no journal access — so it is unit-testable and shared by the apply layer. It returns <see cref="ConflictDecision"/>s only: it never
/// changes money or stock, and no branch removes or replaces a posted financial record (a correction is a new reversing command).
/// </summary>
public sealed class ConflictResolver
{
    /// <summary>Decides the outcome of <paramref name="inbound"/> meeting the pending local <paramref name="overlay"/> (null means none).</summary>
    public ConflictDecision Decide(SyncRecord inbound, PendingOverlay? overlay)
    {
        ArgumentNullException.ThrowIfNull(inbound);
        overlay ??= PendingOverlay.None;
        var cls = ConflictPolicyCatalog.ClassOf(inbound.Entity);

        // The pending intent is what makes a version mismatch a real conflict; without it the server row simply lands in the base.
        if (!overlay.HasPendingIntent) return Apply(cls, "No pending local intent.");

        return cls switch
        {
            ConflictClass.UiPreference => LastWriteWins(inbound, overlay),
            ConflictClass.PostedFinancial => ReversalOnly(),
            ConflictClass.AppendOnlyLedger => Apply(cls, "Append-only: cannot conflict."),
            ConflictClass.InventoryMovement => Additive(),
            ConflictClass.AppointmentSlot => SlotPolicy(inbound, overlay),
            _ => FieldMerge(inbound, overlay),
        };
    }

    private static ConflictDecision Apply(ConflictClass cls, string detail) =>
        new(cls, ConflictAction.ApplyInbound, true, true, null, false, null, false, false, [], [], detail);

    private static ConflictDecision LastWriteWins(SyncRecord inbound, PendingOverlay overlay)
    {
        // The only class where whole-row LWW is allowed. Missing timestamps favour the server row (it is the converged state).
        var localNewer = overlay.LocalUpdatedAtUtc is { } local && inbound.UpdatedAt is { } remote && local > remote;
        return localNewer
            ? new(ConflictClass.UiPreference, ConflictAction.KeepLocal, false, true, null, false, null, false, false, [], [],
                "UI preference: newer local value wins.")
            : Apply(ConflictClass.UiPreference, "UI preference: inbound value is newer or equal.");
    }

    private static ConflictDecision ReversalOnly() =>
        new(ConflictClass.PostedFinancial, ConflictAction.ReversalOnly, true, true, null,
            FlagNeedsAttention: true, FinancialEffect: FinancialEffect.Unknown, RequiresReversingCommand: true,
            RederiveOnHand: false, [], [],
            "Posted financial fact is immutable: flag the local post, confirm before retrying; a correction is a new reversing command.");

    private static ConflictDecision Additive() =>
        new(ConflictClass.InventoryMovement, ConflictAction.ApplyInbound, true, true, null, false, null, false,
            RederiveOnHand: true, [], [], "Movements are additive: both apply, on-hand is re-derived.");

    private static ConflictDecision SlotPolicy(SyncRecord inbound, PendingOverlay overlay) =>
        overlay.CollidesWithInbound && !inbound.Deleted && inbound.Op != 'D'
            ? new(ConflictClass.AppointmentSlot, ConflictAction.RejectLocalIntent, true, true, ErrorCodes.AppointmentSlotConflict,
                false, null, false, false, [], [], "Colliding local slot intent is rejected, never merged.")
            : Apply(ConflictClass.AppointmentSlot, "Slot does not collide with local intent.");

    private static ConflictDecision FieldMerge(SyncRecord inbound, PendingOverlay overlay)
    {
        var touched = overlay.TouchedFields ?? new HashSet<string>(StringComparer.Ordinal);
        var merged = new List<string>();
        var kept = new List<string>();
        if (inbound.Payload is { ValueKind: JsonValueKind.Object } payload)
        {
            foreach (var property in payload.EnumerateObject())
                (touched.Contains(property.Name) ? kept : merged).Add(property.Name);
        }

        return new(ConflictClass.Demographics, ConflictAction.MergeFields, true, true, null, false, null, false, false, merged, kept,
            "Field-level merge: inbound wins for untouched fields, the pending command keeps its own.");
    }
}
