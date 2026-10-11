namespace Animora.Application.Sync;

/// <summary>
/// What the command journal says about the record an inbound change targets. Built by the apply layer from the pending overlay
/// (read-only); the resolver never queries or edits the journal itself.
/// </summary>
/// <param name="HasPendingIntent">A not-yet-settled local command targets this entity.</param>
/// <param name="LocalUpdatedAtUtc">When the local value was last written (for the UI-preference LWW comparison).</param>
/// <param name="TouchedFields">Payload field names the pending local command changes (for the field-level merge).</param>
/// <param name="CollidesWithInbound">For appointment slots: the pending local intent occupies the same slot as the inbound record.</param>
public sealed record PendingOverlay(
    bool HasPendingIntent,
    DateTimeOffset? LocalUpdatedAtUtc = null,
    IReadOnlySet<string>? TouchedFields = null,
    bool CollidesWithInbound = false)
{
    /// <summary>No pending intent: the inbound record meets nothing local.</summary>
    public static readonly PendingOverlay None = new(false);
}
