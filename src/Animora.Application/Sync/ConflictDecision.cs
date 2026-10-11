using Animora.Contracts.Wire.Commands;
using Animora.Domain.Results;

namespace Animora.Application.Sync;

/// <summary>What the apply step must do with one inbound record that meets pending local intent.</summary>
public enum ConflictAction
{
    /// <summary>Apply the inbound row to the replica base; pending local intent is untouched.</summary>
    ApplyInbound = 0,

    /// <summary>Keep the newer local value (last-write-wins, UI preferences only); the inbound row is not applied.</summary>
    KeepLocal = 1,

    /// <summary>Posted financial fact: the inbound row reaches the base, the local post is flagged, and any correction is a NEW reversing command.</summary>
    ReversalOnly = 2,

    /// <summary>The colliding local intent is rejected with <see cref="ConflictDecision.Error"/>; the inbound row is applied.</summary>
    RejectLocalIntent = 3,

    /// <summary>Field-level merge: inbound fields the local command did not touch are applied; the command's fields stay pending.</summary>
    MergeFields = 4,
}

/// <summary>
/// The pure outcome of <see cref="ConflictResolver.Decide"/>. It is a decision only: the resolver never mutates money or stock —
/// financial corrections route back through the command pipeline (two-pipeline rule).
/// </summary>
/// <param name="Class">The data class that decided the outcome.</param>
/// <param name="Action">What to do with the inbound record.</param>
/// <param name="ApplyInbound">Whether the inbound row is written to the replica base.</param>
/// <param name="OverlayPreserved">Always true unless the local intent is explicitly rejected; the pending journal is never edited by a pull.</param>
/// <param name="Error">Rejection code for <see cref="ConflictAction.RejectLocalIntent"/>, otherwise <see langword="null"/>.</param>
/// <param name="FlagNeedsAttention">The local post must surface as <c>NeedsAttention</c> (posted-financial conflicts).</param>
/// <param name="FinancialEffect">The honest financial-effect answer for a flagged local post; <see langword="null"/> when not applicable.</param>
/// <param name="RequiresReversingCommand">A correction, if any, must be a new reversing command — never an in-place edit.</param>
/// <param name="RederiveOnHand">Inventory on-hand must be re-derived from the movements.</param>
/// <param name="MergedFields">Inbound fields applied by a field-level merge (empty otherwise).</param>
/// <param name="KeptLocalFields">Fields left to the pending local command by a field-level merge (empty otherwise).</param>
/// <param name="Detail">Short human/diagnostic explanation.</param>
public sealed record ConflictDecision(
    ConflictClass Class,
    ConflictAction Action,
    bool ApplyInbound,
    bool OverlayPreserved,
    ErrorCode? Error,
    bool FlagNeedsAttention,
    FinancialEffect? FinancialEffect,
    bool RequiresReversingCommand,
    bool RederiveOnHand,
    IReadOnlyList<string> MergedFields,
    IReadOnlyList<string> KeptLocalFields,
    string? Detail);
