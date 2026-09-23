using Animora.Domain.Results;

namespace Animora.Domain.Scheduling;

/// <summary>
/// One existing appointment reduced to just what the conflict check needs: its id, its doctor, its
/// slot and its state. The Application layer builds this list from a tenant-filtered, indexed range
/// query on <c>(TenantId, DoctorUserId, StartUtc)</c> (CONVENTIONS §5) before calling the policy.
/// </summary>
/// <param name="AppointmentId">The existing appointment's id — returned as the conflict detail.</param>
/// <param name="DoctorUserId">Its assigned doctor, or <see langword="null"/> when unassigned.</param>
/// <param name="Slot">Its slot.</param>
/// <param name="State">Its current state — terminal states never conflict.</param>
public readonly record struct ExistingSlot(
    string AppointmentId,
    string? DoctorUserId,
    AppointmentSlot Slot,
    AppointmentState State);

/// <summary>
/// The <b>authoritative</b> appointment slot-conflict rule (ARCHITECTURE.md §8.5): an overlap is a
/// domain validation that rejects, never a last-write-wins resolution.
/// </summary>
/// <remarks>
/// <para>The rules, applied in order to each existing slot:</para>
/// <list type="number">
/// <item>A slot in a terminal state (<c>Cancelled</c>, <c>NoShow</c>, <c>Completed</c>) never conflicts.</item>
/// <item>Two slots conflict only when they overlap <b>and</b> share the same non-null doctor.</item>
/// <item>A candidate with <c>doctorUserId == null</c> (unassigned) never conflicts — it is triaged at reception.</item>
/// <item>On the first conflict, return <c>APPOINTMENT_SLOT_CONFLICT</c> with the conflicting id.</item>
/// </list>
/// <para>
/// The policy is a pure static: no state, no clock, no allocation per candidate — it walks the
/// supplied list with a plain <c>for</c> loop, no LINQ (PERFORMANCE.md §2).
/// </para>
/// </remarks>
public static class SlotConflictPolicy
{
    /// <summary>
    /// Returns <see cref="Result.Ok"/> when <paramref name="candidate"/> is free for
    /// <paramref name="doctorUserId"/>, or <c>APPOINTMENT_SLOT_CONFLICT</c> (with the conflicting
    /// appointment id as the detail) on the first blocking overlap.
    /// </summary>
    /// <param name="candidate">The slot being booked or moved into.</param>
    /// <param name="doctorUserId">The candidate's doctor; <see langword="null"/> never conflicts.</param>
    /// <param name="existing">The tenant's other appointments in the relevant window.</param>
    public static Result EnsureFree(
        AppointmentSlot candidate,
        string? doctorUserId,
        IReadOnlyList<ExistingSlot> existing)
    {
        ArgumentNullException.ThrowIfNull(existing);

        // Rule 3: an unassigned candidate is triaged at reception and never conflicts.
        if (string.IsNullOrEmpty(doctorUserId))
        {
            return Result.Ok();
        }

        for (var i = 0; i < existing.Count; i++)
        {
            var other = existing[i];

            // Rule 1: terminal slots never conflict.
            if (other.State is AppointmentState.Cancelled
                or AppointmentState.NoShow
                or AppointmentState.Completed)
            {
                continue;
            }

            // Rule 2: only same-doctor overlaps conflict.
            if (!string.Equals(other.DoctorUserId, doctorUserId, StringComparison.Ordinal))
            {
                continue;
            }

            if (candidate.Overlaps(other.Slot))
            {
                // Rule 4.
                return Result.Fail(ErrorCodes.AppointmentSlotConflict, other.AppointmentId);
            }
        }

        return Result.Ok();
    }
}
